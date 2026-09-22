# Runtime memory investigation — September 21, 2026

## Confirmed production failure

The route chunk revision
`amftms-api-b-862d8158-9127-4dbd-8a2e-d534f7fd913e` exceeded its 512 MiB limit.
Cloud Run recorded 530 MiB at `2026-09-21T21:47:48.663416Z`, followed by a new
instance at `21:47:53.815304Z`. Minute samples reached 476.60 MiB before the
recorded drop to 367.69 MiB. That drop must not be attributed to successful GC:
the platform restart is directly established by the logs.
Event evidence: `artifacts/managed/diagnostic-LJcOL0/events.json`.

The earlier [release observations](route-chunks-release-2026-09-21.md) ended
before this failure. Their short-window limitation matters: the release did not
establish sufficient headroom or a long-term memory reduction. The newer metric
sample is retained under `artifacts/managed/diagnostic-Gc5ASu/memory.json`.

An OOM proves that the container exceeded its budget. It does not identify the
owner, prove a leak or justify attributing hundreds of MiB to HOS. Existing
route measurements describe a local object-graph scenario, not the deployed
process's complete retained heap. Cache limits are not measured occupancies.

## Prepared instrumentation

The protected memory query follows the existing API → MediatR → Application
diagnostics path. Infrastructure supplies runtime/cgroup counters through
`IRuntimeMemoryReader`. Cache owners expose aggregate statistics through
`ICacheMemorySource`; six byte-budget partitions, two ETA work-unit caches,
the current ETA entry count and shared cache count are distinguishable.

`TrackStatistics` is enabled on the existing caches. Their budgets, expiry,
ownership and eviction behavior are unchanged. The query does not enumerate
cached payloads, force collection, start workers, call providers or access SQL.
No periodic sampling worker or additional cloud service is introduced.

The runtime result identifies process start, working set, approximate managed
bytes, cumulative allocation, collection counts and last-GC heap/fragmentation/
committed bytes. Linux cgroup counters supplement those when available. Unknown
cache sizes and unavailable OS counters remain null, never a measured zero.
This supports the first separation of retention, allocation pressure and memory
outside the managed heap. Object-type/root attribution still needs a heap profile
if aggregate counters do not isolate the cause.

## Verification and publication

The affected Synchronization group passed 828 Server and 460 Client tests,
plus its JavaScript checks and architecture checks. New checks cover registered
cache ownership, HOS occupied size versus capacity, unknown/shared byte sizes,
cgroup v1/v2 parsing, cancellation and the Admin-only endpoint boundary.
Evidence: `artifacts/managed/diagnostic-JVCzfU/tests.log`.

The full local release gate passed 3,145 Server, 1,052 Client C# and 625
JavaScript tests (4,822 total), with no failures or skips. It verified all 297
published assets and 11 JavaScript entry-point dependency graphs. Evidence:
`artifacts/managed/diagnostic-iJLujo/release.log`, pinned with `.keep`.
CSharpier and `git diff --check` passed. Browser checks were not run because
the change adds no UI.

The approved diagnostic deployment completed as revision
`amftms-api-b-411eb4e0-9584-4bca-98bb-2d1007ee0dc8`, verified at 100% traffic.
Image digest:
`sha256:016d77c0db8140c02cae20dffe24dcd9859c4a3ff886c6c4d79951a6d5368fac`.
Deployment evidence: `artifacts/managed/diagnostic-54TFOD/deploy.log`.
No memory limit, application database or provider configuration was changed.

## Initial runtime observations

Samples are pinned in `artifacts/managed/diagnostic-slVQkh/runtime.jsonl`;
stage counts are beside them. At 22:19:42 UTC, container usage was 463.3 MiB,
managed bytes approximately 133.3 MiB and measured byte-cache admission sizes
13.9 MiB. HOS history occupied approximately 64 KiB. The peak observed by
22:23:28 was 470.6 MiB, with the same process start time throughout.
The 22:23:28 container sample fell to 452.9 MiB without a restart. This does
not establish long-term stability. Cgroup component counters were unavailable.
The shared cache and ETA dictionaries do not expose retained byte sizes.

## Local allocation decomposition

`tools/RouteChunkMemoryProbe --allocations-read-only` loads saved rows under
an explicit read-only transaction and measures pure operations after rollback.
Evidence: `artifacts/managed/diagnostic-TwXJ5k/allocations.jsonl`.
Across all 15 saved plans, including completed work, one pass allocated:

| Stage | Allocated MiB | Largest single plan, MiB |
| --- | ---: | ---: |
| Full decode | 26.72 | 3.27 |
| Exact index | 2.58 | 0.39 |
| Cold display, including decode/index | 34.47 | 4.07 |
| Warm display decode | 9.95 | 1.07 |
| Warm metadata decode | 0.20 | 0.02 |
| State serialization | 0.30 | 0.04 |

Stages overlap and must not be summed. This is not the production active fleet
cycle or retained memory. Local synchronous measurements use thread allocation
counters, three warm-ups and twenty repetitions.

A concrete unnecessary decode exists in `PlanningCurrency.IsCurrentAsync`:
it calls `RoutePlanStorage.Read` to inspect completion/assignment/input currency,
then the tracking caller decodes its current plan again. The existing
`ISavedRoutePlanReader` and metadata overload of `PlanningWorkPolicy.IsCompleted`
provide the intended compact read boundary. Replacing this path needs regression
coverage for legacy/native identity, changed profiles, missing saved plans and
completed predecessors; no completion/publication guard should be relaxed.
This finding does not attribute the full production allocation rate or OOM.

## Concurrency review

Route builds and progress share `BuildGates` keyed by truck. Fuel search/edit
uses a truck gate and two global search slots. ETA refresh has two workers;
planning refresh uses configured bounded consumers (default two). Cold route
and fuel caches each permit two loads. These are separate pools, not one global
memory budget. Source inspection establishes these bounds, not the observed
production overlap. Adding a global gate without a workload profile could
increase waiting while leaving repeated deserialization unchanged.


## Local saved-preview cycle

The provider-free saved fleet-preview path returned four routes. A cold
Application container allocated 54.67 MiB, including EF/JSON initialization;
three subsequent reads allocated 4.79, 4.70 and 4.70 MiB. Warm elapsed times
were 20.3, 12.6 and 15.2 ms locally. The fleet cache stores JSON and reconstructs
the response object graph on each hit; it is clone-safe, not allocation-free.
Evidence: `artifacts/managed/diagnostic-rVthf1/preview.jsonl`.

The diagnostic uses a read-only repeatable-read PostgreSQL transaction and
starts no API host or background worker. No production writes or provider calls
were made by these two successful probes. Release compilation and execution
of both modes passed, as did CSharpier and `git diff --check`. No production
business code was changed in this follow-up, so the application test suite was
not repeated. This is an operation allocation decomposition, not an EventPipe
allocation-stack trace or a native-memory profile. The complete OOM owner
remains unproven; neither local warm timings nor these allocation totals prove
Cloud Run capacity for 100 trucks.

## Compact currency implementation

`PlanningCurrency` now reads compact saved-route metadata through the existing
Infrastructure reader. `RoutePlanStore` requires that interface explicitly and
uses the existing route cache group with a separate metadata entry. Geometry
and metadata share write invalidation; metadata hits remain clone-safe and do
not add a database query on every poll. Input matching shares the full-route
legacy signature rule, including rejection after route-choice or manual-stop
changes. Native identity and assignment-revision checks remain in the policy.

A repeat of the pure allocation probe with compact metadata projected from the
same 15 PostgreSQL rows allocated 30,560 bytes total for metadata decoding,
compared with 28,016,632 bytes for full route decoding (about 99.89% less for
this operation). Largest per-plan values were 3,280 versus 3,425,228 bytes.
Evidence: `artifacts/managed/diagnostic-fhm9wR/allocations.jsonl`.
Database queries, cache overhead and input-signature calculation are outside
this comparison. It is not a prediction of whole-process RSS reduction and
does not establish that the OOM is fixed. No schema migration is required.

Validation of the compact currency change: 14 new regression cases cover legacy
signature compatibility, legacy/native current-work selection, completion,
profile changes, identity/revision mismatch, absent plans, clone isolation and
shared cache invalidation. An unreadable geometry manifest proves the currency
path does not hydrate geometry. The final `bash test.sh all` run passed 3,159
Server, 1,052 Client and 625 JavaScript tests with no skips or failures, including
architecture checks. Evidence: `artifacts/managed/diagnostic-MPWAw9/tests.log`.
An earlier full run passed Server tests but failed one existing Client forecast
publication test on its asynchronous assertion timeout; it passed on the final
run without changes to that test or Client code. Formatting and diff checks
passed. This optimization is local and has not been deployed; production RSS
after the change remains unmeasured. No migration is pending for this change.

The original diagnostic sample completed 197 observations over approximately
18 minutes, ending at 22:30:26 UTC. Peak observed container usage was 470.6 MiB;
all observations had the same process start. This predates the compact currency
change and must not be presented as its result.

## Approved compact-currency deployment

The subsequent explicit publication request deployed build
`0d01fcc4-b5ee-48ef-8b3e-d9d99594b259` as revision
`amftms-api-b-0d01fcc4-b5ee-48ef-8b3e-d9d99594b259`.
Digest:
`sha256:33802ffc9cee3c10703c8a67aab5dbd2cc0af355652d8047d980c86f88ed4864`.
The wrapper verified readiness and 100% traffic; `/api/health/live` returned 200.
The memory limit remains 512 MiB. No migration or Firebase publication was needed.
Deployment log: `artifacts/managed/diagnostic-3UQzab/deploy.log`.

Cloud Build ran the release gate: 625 JavaScript, 1,052 Client and 3,135 Server
tests passed; 20 PostgreSQL tests were skipped without the isolated fixture.
The cloud artifact phase verified 255 assets and 11 entry-point graphs.
Its platform/configuration test inventory differs from the local full run.
Build log: `artifacts/managed/diagnostic-OHLRMs/build.log`.

While the build ran, the previous diagnostic revision exceeded memory again:
Cloud Run recorded 515 MiB at `22:44:37.099921Z` and restarted it. At
`22:44:31.4845882Z`, container usage was 483.8 MiB, managed bytes 130.4 MiB,
GC committed memory 143.6 MiB and measured cache budgets about 10.8 MiB.
This was approximately 33 minutes after process start, beyond the first
18-minute observation. Event evidence: `diagnostic-o9VKPg/events.json` under
`artifacts/managed`; pre-release counters: `diagnostic-BOBaXu/runtime.jsonl`.
The shared-cache byte size remains unknown. The readings alone do not identify
native allocations or prove a managed leak.

Post-publication counters were collected under
`artifacts/managed/diagnostic-eSaxdN/`, against a process started at
`2026-09-21T22:51:39.7083972Z`. Those samples must not be mixed with the old
revision's startup or its restart during the pre-release baseline.


The post-release collector completed 191 successful samples from 22:52:19 to
23:10:14 UTC (approximately 18 minutes), with no HTTP/transport errors and one
unchanged process start. Final container usage was 441.8 MiB, peak 447.5 MiB,
managed-byte estimate 87.9 MiB, GC committed memory 145.3 MiB and measured
byte-cache admission sizes 10.2 MiB. Final liveness returned HTTP 200.
Summary: `artifacts/managed/diagnostic-eSaxdN/summary.json`.
Cloud Run logs checked near the end showed only the initial startup and no
memory-limit event for the new revision.

The prior 18-minute window peaked at 470.6 MiB, but traffic/stage counts differ
between windows. This is not a controlled 23 MiB saving. The previous revision
failed after approximately 33 minutes, so this window cannot exclude another
late OOM. The compact-read optimization is deployed and its local allocation
benefit is measured; the complete production memory problem remains open.
The next attribution step is a native/runtime memory breakdown and an
allocation-stack profile, not an assumption that the small HOS cache is costly.
