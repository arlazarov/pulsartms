# Localhost performance audit — September 12, 2026

## Scope and safety

Investigated first-open Fleet Map/HOS/route latency, Dispatch loading, server work,
response sizes and potential memory retention. This was a diagnostic pass, not an
optimization release. No application behavior, deployment, database rows or
migrations were changed, and the running API/Client were not restarted during it.

The development Client uses port 5067, the API port 5086, and the configured remote
Neon database. Synchronization is disabled locally, so demand-driven refresh paths
matter. Findings are for this working copy, not production capacity estimates.

The diagnostic addition is `tools/RouteMemoryProbe/PerformanceReadProbe.cs`, selected
with `--read-only --performance-reads`. It invokes existing read handlers in a new
service provider without starting hosted workers. All database contexts share an
explicit read-only transaction with a ten-second statement timeout. It also makes
one bounded Samsara HOS read per run. It does not invoke route providers or save
plans. Only timings, table names, counts and serialized sizes are printed.

An initial connection attempt failed because the Neon pooled endpoint rejected a
startup transaction option. A subsequent probe completed the board/preview/HOS
samples but could not sample concurrent telemetry on its shared connection. That
was a diagnostic-harness limitation, not evidence of an application failure. The
corrected probe omits that sample and completed successfully with rollback.

## Read-path measurements

Sampled four trucks and five loads for the local date. Values below are from the
final completed run; an earlier run reproduced the same main bottleneck.

| Read | Elapsed | SQL commands | Sum of SQL command time |
| --- | ---: | ---: | ---: |
| Full board, cold process/application caches | 3,832 ms | 37 | 2,791 ms |
| Full board, warm 1 | 2,473 ms | 30 | 2,406 ms |
| Full board, warm 2 | 2,134 ms | 30 | 2,038 ms |
| Warm board, ETA excluded | 495 ms | 7 | 449 ms |
| Warm board, financials excluded | 2,655 ms | 25 | 2,642 ms |
| Warm board, ETA and financials excluded | 83 ms | 2 | 80 ms |
| Warm board, identities only | 0.9 ms | 0 | 0 ms |
| Truck 11007 saved preview, cold application cache | 777 ms | 8 | 633 ms |
| Same preview, warm 1 / warm 2 | 91 / 89 ms | 2 / 2 | 76 / 75 ms |
| Fleet previews after the single-truck read | 888 ms | 14 | 706 ms |
| HOS provider read, including credential lookup | 703 ms | 1 | 40 ms |

Earlier full-board samples were 3,700 ms cold and 2,237/2,178 ms warm. Excluding ETA
took 381 ms; the core-only sample took 79 ms. These are diagnostic exclusions of
functionality, **not achieved improvements or promises of final UI latency**.

Cold means a fresh diagnostic process/application cache, not a flushed database or
browser cache. SQL command timings include remote database/network wait, not just
PostgreSQL execution. The pinned connection excludes connection-pool acquisition.
The probe excludes HTTP authentication, transfer, browser rendering and Google
Maps startup. Its board HOS snapshot starts empty. These are small samples, not
p95/p99 benchmarks or tests against a disposable database fixture.

## Confirmed causes and risks

### Dispatch waits for per-truck ETA validation

`GetDispatchBoardHandler` waits for financial hydration and
`EtaForecastService.PopulateAsync` before returning any rows. ETA validation calls
`EtaChainInputsService.DescribeAsync` sequentially per truck. That reloads stops,
resolves assignments, checks root-route metadata and future route versions, and
reads driver associations even though the board already hydrated related data.

Four root-route metadata commands alone took 930–1,159 ms in the final warm full
samples. They extract fields from `DispatchRoutePlans.PlanJson` in PostgreSQL;
geometry is not transferred by those metadata queries. The precise database
execution/JSON extraction breakdown was not measured. Nine dispatch/stop reads
added 377–446 ms. The warm page still required 30 database commands for four trucks.

The Client waits for the complete board before rendering rows. Its separate board
planning read then obtains another board with ETA validation and iterates truck
planning reads. Moving expensive columns off the initial critical path and batching
authoritative validation are separate opportunities; neither requires weakening
ETA freshness or assignment validation.

### Cold HOS is delayed by delivery cadence, not only Samsara

`DriverHosSnapshot.GetClocksAsync` returns the current in-memory snapshot immediately
and signals background demand. A cold snapshot is empty. Successful background
refresh is scheduled at 45-second intervals and only samples less than a minute
old are returned. No immediate completion notification reaches the Client.

Fleet Map receives HOS through the live planning response. Cold selection first
awaits a saved preview, up to two seconds, then starts live planning. Subsequent
reads run on the ten-second truck polling loop, after the GPS read. A newly ready
HOS snapshot can therefore wait for another poll and another planning read.

The actual API runtime sample recorded a 510 ms HOS provider-wait stage; separate
read probes measured 664–703 ms including credentials. These do not measure time
until the clocks appear on screen, but they support removing the unnecessary
preview/planning/poll dependency instead of simply increasing Samsara request rate.

Dispatch updates `truck.Hos` with its full board response, normally once per minute.
The planning-summary response contains HOS, but `RefreshPlanningAsync` does not
merge it into that row property; telemetry also does not update HOS. This creates
another cold-start delivery delay.

### Initial map and route reads have serial dependencies

`InitializeMapAsync` starts location polling only after the Google Maps session is
ready. Data fetching and map-library initialization do not overlap. Cold truck
selection waits for its saved preview before live planning starts. The preview
does not contain HOS.

The route refresh worker processes its queue one dispatch at a time. A slow job
can delay unrelated queued trucks; the local timeout is 180 seconds, normal
cooldown 120 seconds and retry interval 60 seconds. TomTom also has bounded slots
and request-budget reservation gates. These constraints are visible in code, but
this audit did not force a route recalculation or capture a TomTom provider-body
sample. It does **not** establish which stage dominates a fresh paid route build.

### Network volume and redundant work

Uncompressed diagnostic JSON sizes were approximately:

| Response | Serialized bytes |
| --- | ---: |
| Full Dispatch board | 37,042 |
| Core board | 21,294 |
| One saved truck preview | 639,755 |
| Four saved fleet previews | 2,491,658 |

Board JSON serialization was below one millisecond when warm. It is not the main
board bottleneck. Route snapshots are materially larger: the single preview had
4,431 display leg points, plus the other plan/reference data. These sizes are not
compressed HTTP transfer measurements.

Existing safeguards already omit unchanged geometry using known plan ID/version,
return metadata-only board planning summaries, simplify display geometry, coalesce
same-URL planning reads and pause page polling while hidden. Preserve them.
`PlanningDisplayCache.PreloadAsync` has no Client caller; enabling an unconditional
whole-fleet preload would add roughly 2.5 MB of uncompressed data in this sample,
so it is not a default recommendation for faster first paint.

`PlanningDisplayCache.RefreshAsync` cancels only an individual caller's wait.
`CompleteRefreshAsync` uses `CancellationToken.None` for the underlying HTTP read.
Switching trucks or leaving the page can therefore leave now-unneeded work in
flight. Cancellation must respect shared consumers and must not conflate disposable
display reads with committed route-edit operations.

## Memory and CPU observation

Attached `dotnet-counters` to the existing API PID 60942 for 45 seconds, from
21:16:05 to 21:16:50 local time. Installed the tool only in a managed scratch
directory. The managed diagnostic output was
`artifacts/managed/diagnostic-MWVBIp/runtime.json` and is subject to normal retention.

- Working set rose from 150.8 to 323.9 MiB.
- Allocation counters recorded 156.8 MiB over the 44 rate intervals, approximately
  3.6 MiB/second; the largest interval allocated 46.0 MiB.
- No GC collections or GC pauses were reported during the sampled rate intervals.
- Last-collection heap counters stayed unchanged: LOH 291.6 MiB and total heap
  approximately 343.9 MiB. These describe the last collection, not current retained
  live objects. Committed GC memory was 584 MiB; it is not the process working set.
- CPU consumed approximately 0.86 core-seconds during the 44 rate intervals. No
  thread-pool queue buildup was observed in this small window.
- Live planning request histogram intervals reported approximately 0.50–1.38 s;
  saved preview intervals reported 0.09–0.64 s. Histogram interval samples are not
  raw request counts or an end-to-end browser latency distribution.

This shows allocation/large-object pressure worth investigating, **not a confirmed
memory leak**. There was no post-GC retained-heap comparison, long-duration steady
state test, or authenticated browser heap/navigation test. Low CPU in this window
also does not prove CPU will remain low during route optimization or fleet growth.

Inspected caches have explicit bounds: server read/display caches, fuel memory,
HOS snapshot, and Client planning geometry/entry limits. Map component disposal
cancels owned work and disposes its session and visibility observer. ETA demand
entries expire through its worker. These protections reduce obvious risks but do
not prove all references are released under repeated navigation.

## Follow-up: actual traffic and per-route memory

The user also requested realistic traffic and per-truck memory estimates. Additional
read-only measurements distinguish observed socket traffic from payload estimates.

### API socket traffic

Two concurrent `nettop` process-summary samples observed ten readings at five-second
intervals (approximately 45 seconds), restricted to the running API PID 60942.
The separate diagnostic processes are not included in those counters.

| Direction | Counter increase |
| --- | ---: |
| Local clients to API, loopback | 31,723 bytes |
| API to local clients, loopback | 774,581 bytes |
| External connections to API | 2,230,366 bytes |
| API to external connections | 197,969 bytes |

The outgoing local API traffic corresponds to about 1.03 decimal MB/minute,
59 MiB/hour or 473 MiB/eight hours **only if this observed activity continues**.
This is a total for the local API and connected clients, not a per-truck or
per-user allowance. It excludes the Client development server's static assets and
the browser's direct Google Maps traffic. It is not a production billing forecast.

A second, separate approximately 45-second connection-level sample classified
remote ports without recording addresses or packet contents. PostgreSQL connections
received 1,842,055 bytes and sent 25,683 bytes; HTTPS provider connections received
98,441 bytes and sent 3,237 bytes. About 95% of the observed external incoming bytes
in that sample were database traffic. Connection-level totals can miss closed
connections; these samples are not a full packet capture or SQL attribution trace.

### Initial Client download

The diagnostic browser, on the anonymous local Login page, retained Resource Timing
entries for 209 framework resources with 25,404,021 transfer bytes (24.23 MiB), plus
242,766 bytes for seven other local assets. The largest included CoreLib (4.87 MB)
and XML (3.10 MB). This is a development startup observation, not an authenticated
map or published Release measurement. Framework download is shared by the whole
application and normally cached; it must not be charged to every truck or poll.
Google Maps downloads were not captured by this anonymous session.

### Current route memory, measured separately

Ran the existing bounded `RouteMemoryProbe --read-only` in its own process. It
loads saved JSON, warms the measurement, and uses `GC.GetTotalMemory(true)` with
the measured object kept alive. Forced collections occurred only in this disposable
diagnostic process, never in the running API. Values are approximate managed object
retention deltas, not whole-process or browser RAM.

| Truck / current load | Compact display snapshot retained | Full deserialized plan retained | Temporary allocations to read display JSON |
| --- | ---: | ---: | ---: |
| 11005 / 1379 | 0.98 MiB | 1.80 MiB | 1.41 MiB |
| 11006 / 1382 | 2.70 MiB | 6.66 MiB | 3.90 MiB |
| 11007 / 1383 | 1.27 MiB | 3.01 MiB | 2.21 MiB |
| 54777 / 1376 | 1.28 MiB | 1.54 MiB | 1.08 MiB |

The snapshot includes indexed exact geometry and display/metadata JSON. The full
plan measurement excludes its already-existing source JSON string; for example,
11006's source string alone occupies about 6.06 MiB. These columns describe
different representations, not universally simultaneous allocations to sum into
a fixed cost per truck. Known-version reads can use metadata instead of reading
full display JSON, so the last column is not a cost paid on every poll.

The four independently measured current display snapshots sum to about 6.22 MiB.
Their allocation cost depends on route length and reference geometry, not truck
identity. All 16 saved plans, including other loads, contain 23,876,416 bytes of JSON
in storage; this is neither resident RAM nor authorization to delete history.
The 32 MiB display cache bounds retention, but a larger active fleet could increase
evictions and database reloads. Linear extrapolation to hundreds of resident truck
routes would ignore that cache policy.

### Per-truck response sizes and compression experiment

`--performance-reads --payloads-only` reads each saved preview and then its planning
state with known plan ID/version. It runs without hosted workers in the read-only
transaction. HOS/ETA caches are empty in this process; all four updates contained
fuel data and omitted geometry, but did not contain ETA. This is a lower-content
comparison, not the complete authenticated live response.

| Truck | Initial preview JSON | Offline Gzip Fastest | Known-version update JSON | Offline Gzip Fastest update |
| --- | ---: | ---: | ---: | ---: |
| 11005 | 384,127 B | 97,402 B | 13,297 B | 5,196 B |
| 11006 | 1,152,104 B | 284,147 B | 18,645 B | 6,298 B |
| 11007 | 639,755 B | 160,324 B | 13,400 B | 5,171 B |
| 54777 | 315,667 B | 79,961 B | 17,307 B | 6,052 B |

Offline Brotli Fastest preview sizes were 64,445 / 197,407 / 110,168 / 55,223 bytes
respectively. Thus these preview bodies compressed by about 75% with Gzip or
83% with Brotli. No API compression registration was found in the inspected server
source; hosting-edge behavior and authenticated `Content-Encoding` were not
verified. These savings have not been implemented or observed on the wire.

At exactly one such update every ten seconds for eight hours, update bodies alone
would total 36.5–51.2 MiB per continuously selected truck/view, or 14.2–17.3 MiB with
the measured Gzip sizes. This scenario excludes ETA/HOS fields, GPS, request/response
headers, initial previews, route version changes, station layers and map assets.
It must not be substituted for the measured aggregate API traffic above.

## Recommended implementation order

1. **Independent, shared HOS delivery.** Trigger bounded demand when fleet data is
   opened and deliver fresh clocks without waiting for route geometry/ETA. Use a
   limited pending-only follow-up or notification, not permanently faster polling.
   Merge fresh HOS into Dispatch rows. Keep timestamps and explicit stale states.
2. **Fast initial Dispatch data, independent enrichment.** Keep a stable layout
   while ETA and financial values arrive. Batch/reuse page input reads behind
   Application interfaces; preserve all financial computation on the server.
3. **Cheaper authoritative ETA validation.** Batch root metadata/assignment/driver
   reads and avoid repeated large-JSON extraction where a versioned projection can
   safely replace it. Any persistence change needs separate approval and tests.
4. **Prioritize selected-truck reads and remove duplicated work.** Overlap safe map
   initialization/data fetches, avoid serial preview-to-HOS dependencies, retain
   known-version geometry reuse, and cancel display reads with no remaining
   consumer. Do not blanket-prefetch all routes or increase provider concurrency
   without budget and queue measurements.
5. **Allocation and lifecycle verification.** Measure large allocations by type and
   stage, then repeat map/Dispatch navigation and truck selection with an
   authenticated browser. Compare settled post-GC retention and active listeners,
   overlays, requests and cache counts. Reduce route JSON/object copies before
   increasing cache limits; verify correctness and bytes per visible refresh.
6. **Measure real route builds separately.** On an authorized user recalculation,
   capture queue wait, budget/slot wait, provider, persistence, fuel and ETA stages.
   Add bounded per-truck scheduling/coalescing only after identifying the dominant
   stage; preserve current-position routing and provider limits.
7. **Reduce bytes at their source.** Measure compression at the actual API/hosting
   boundary, keep static framework assets cacheable, and compare a local Release
   build with the development startup. Split frequently changing GPS/HOS/progress
   from route geometry and less-frequent fuel/ETA metadata. Prioritize avoiding
   repeated database JSON reads as well as browser downloads; wire compression
   alone will not remove database round trips or object allocations.

## Checks and limitations

The corrected read probe, payload probe and existing memory probe compiled and
completed successfully. `bash test.sh architecture` passed 53 Server, 2 Client C#
and 46 JavaScript checks. These are architecture-only results, not a full pass.
No full regression suite, authenticated browser visual/heap test, isolated
PostgreSQL fixture test, production compression verification or route-provider
performance test was performed. Socket traffic and offline compression experiments
are documented above with their different scopes. No optimization speedup or
fleet-scale capacity has been established.
