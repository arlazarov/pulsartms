# CPU and memory, 10-truck fixture (September 24, night)

Local only. The setup was the real API on the isolated fixture schema, with
synthetic routing and HOS providers, in a Linux ARM64 container limited to
one CPU and 1 GiB (`tools/FleetLoadProbe`). No production database,
provider or deployment was involved. The fixture's synthetic routes are
about 4,000 km with ~75,000 points each, larger than most real loads.
Samples are about every 3 seconds and can miss short peaks.

## Load run (build at eae82c2f)

Five dispatchers polled for 180 s: locations, truck planning, the Dispatch
board, the Messages unread notice and inbox. Telemetry and planning
enqueues ran every 30 s. All 697 requests passed and the queue drained
with no retries.

| Phase | Container peak MiB | CPU avg / p95 (% of one CPU) | Allocation MB/s | Gen0 per min | Gen2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Cold preparation | 638 | 41 / 67 | 67.7 | 515 | 53 |
| Steady (5 dispatchers) | 720 | 34 / 62 | 58.2 | 390 | 80 |
| Drain | 684 | 25 / 41 | 42.9 | 292 | 6 |
| Idle, no clients | 651 | 11.5 / 28 | 13.6 | 91 | 32 |

At idle the process ended with a working set of 634 MiB, a managed estimate
of 207, a heap after the last GC of 349 and GC committed 368. These overlap
and must not be added.

| Request (steady) | Median ms | p95 ms |
| --- | ---: | ---: |
| Dispatch board | 1,328 | 2,074 |
| Messages inbox (no conversations) | 293 | 492 |
| Messages unread notice | 252 | 375 |
| Fleet locations | 65 | 247 |
| Truck planning read | 39 | 555 |

The board and the empty inbox are foreground costs dominated by database
round trips to the remote fixture. They were measured but not investigated
tonight.

## Foreground: what a page actually asks

The table above timed `/api/dispatch/board` with the API's defaults: HOS,
financials and ETA all included. The Dispatch page does not send that. It
asks for a light board and fetches financial and ETA enrichment
separately. Measured per request on the same fixture, warm, with database
commands counted by `X-Probe-Measure`:

| Request | Commands before | Commands after `e7aa35aa` | Time after (ms) |
| --- | ---: | ---: | ---: |
| Light board (what the page sends) | 4 | 3 | ~260 |
| Financial enrichment | 4 | 3 | ~330 |
| ETA enrichment | 21–22 | 20 | 1,130–1,470 |
| API-default board | 23–24 | 22 | 1,150–1,310 |
| Messages inbox | 3 | 2 | ~275 |
| Messages unread notice | 2 | 2 | ~270 |

Against this remote fixture each command costs about 50–100 ms.

- **The driver-group scope (added the same night)** cost one round trip on
  every scoped read, including for dispatchers without a group. `e7aa35aa`
  serves the choice from the read cache, invalidated by the group commands.
- **ETA enrichment** re-runs the board and then its own reads. It is the
  page's real foreground cost; it is owned by the fleet-efficiency read
  path and was not changed.
- **The unread notice** is two commands. One maps the signed-in identity to
  a user id; four modules each have their own copy of that lookup, and
  caching it across requests would delay a deactivation. Not changed.

## Idle: attributed by an allocation trace

With no clients and the queue drained, the idle process still used about
12% of a CPU and 13–15 MB/s of allocation. An EventPipe GC allocation trace
of 188 s (`run.py restart --trace`) attributed it:

| Owner (nearest application frame) | MB sampled |
| --- | ---: |
| `SavedRouteReader.Read`: base road re-parsed to attach a display reference on each plan read | 880 |
| No application frame (framework, GC, runtime) | 815 |
| `PlanningSummaryCache.Complete`: summary serialization every 30 s per running truck | 269 |
| `RouteChunkPacker.Decode` | 203 |
| `RouteDisplayCache.Snapshot.ReadPlan` | 178 |

Most of the first line was System.Text.Json building the positional
`RoutePoint` record through its constructor: `ArgumentState` (379 MB) and
`Arguments<…>` (321 MB).

My first estimate, 40 MB per truck per 30-second cycle, all from summary
serialization, was arithmetic from the total rate and code reading. The
trace showed serialization is 10% of idle allocation, not most of it.

## Fix and before/after

`a972cb54` gives the routing JSON options a `RoutePoint` converter. It writes
the same bytes and reads the same shapes the defaults did. On the same
fixture and schema, idle, 190 s windows:

| | Before | After |
| --- | ---: | ---: |
| Allocation, MB/s | 14.5 | 9.9 (−32%) |
| `SavedRouteReader.Read`, MB | 880 | 364 |
| Constructor-argument types, MB | 700 | 0 |
| Idle CPU, % of one CPU | 11.8 | 12.4 (no improvement; within noise) |

The after build also contains this evening's other commits; none of them
is on this path. Unit measurement: a 20,000-point road reads with 1.17 MB
instead of 3.25 MB.

## A longer run and a restart (build at `9fdca76b`)

This run had 20 minutes of five dispatchers, telemetry and planning
enqueues, then 10 minutes idle, on the same fixture. All 4,796 requests
passed; the queue drained with no retries.

| Minute | Container MiB | Heap after GC MiB | Phase |
| ---: | ---: | ---: | --- |
| 0 | 449 | 273 | start |
| 3 | 575 | 393 | steady |
| 9 | 560 | 365 | steady |
| 15 | 666 | 446 | steady |
| 18 | 573 | 390 | steady |
| 21 | 482 | 237 | idle |
| 30 | 481 | 247 | idle |

- **Memory:** it went up and down within the same band and did not
  climb. That rules out growth over 30 minutes, not over hours.
- **CPU:** steady averaged 33% of one CPU (p95 49); idle averaged 11.6%.
  Gen2 collections at idle fell to about 7.6 a minute, from about 11 in the
  first run.
- **Requests:** steady medians were inbox 199 ms, unread 198 ms, locations
  8 ms and truck planning 37 ms; the API-default board took 1,026 ms.

**Restart.** The same schema, a new process:
- ready in 2.6 s;
- the first light board took 829 ms, then 150–260 ms;
- ETA enrichment took 1.2–1.4 s cold or warm (it is not cached);
- the first inbox took 351 ms.

The first truck-planning read took 30 ms. With the summary cache empty, that
is likely a "being prepared" answer, not a full plan; it was not checked.

## Not measured, or left

- **Why a plan read re-parses the base road.** Proposal in the
  [cohesion review](cohesion-review-2026-09-24.md). Removing that read would
  be the next idle reduction.
- **Idle CPU, attributed later in the night.** An idle window was traced
  with the sample profiler (`run.py restart --trace --sample-cpu`, 187 s,
  build `9fdca76b`). While sampling, CPU read 19%, against 12% without
  sampling.
  - *Managed code is a small share:* about 32 samples a second, roughly 3%
    of one CPU. The rest is runtime work these samples do not attribute
    (GC, timers, I/O).
  - *Within managed code:* `DisplayRouteGeometry.Simplify` 15%, route point
    reads 7.7%, summary serialization 4.3% and chunk decoding 3.8%.
  - *By outermost operation:* execution reads 19.5%, plan loads 9.8% and
    `ReadReferenceAsync` 8.3%.

  The base road re-read and re-simplified for display on every summary
  refresh leads idle CPU as well as idle allocation.
- **Hours-long runs and a 512 MiB limit** were not tested (see the
  30-minute run above).
- **Foreground costs.** Beyond the section above (ETA enrichment's 20
  commands, the identity lookup), not investigated.
- **Cloud Run.** Memory and CPU there, and real routes and real provider
  latency, cannot be inferred from this fixture.

## Side findings

- **Two transient HTTP 500s.** On one fresh fixture, two concurrent plan
  preparations answered 500 after about 8 s. The server log was lost with
  the restart that followed. A rerun on another fresh fixture prepared all
  ten trucks with no error logged. Seen once, not reproduced, not
  explained.

- **The probe API did not exit on SIGTERM within 90 seconds.** Docker
  killed it both times (exit 137), and a summary refresh logged an
  `ObjectDisposedException` during shutdown. The probe ran its planning
  workers beside the host, and `RunAsync` disposed the host before they
  were cancelled. Fixed in the probe: it now stops the workers before
  disposal and exits in about a second (exit 0, no exception). Production
  runs these workers as hosted services and was not affected by this
  code; its own shutdown time was not measured.
- **A stray command.** While re-running the trace I ran a stray
  `docker run hello-world` by mistake. A `hello-world` image is present
  locally; whether this pulled it or it was there before is unknown. It was
  not removed.
