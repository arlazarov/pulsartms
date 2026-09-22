# Route chunks and fleet measurements — 2026-09-21

## Scope

Local implementation on top of `80216a8`; no application-database migration,
Cloud Run deployment, memory increase or localhost startup was performed.
The subsequently authorized migration and publication are recorded in the
[release report](route-chunks-release-2026-09-21.md).
The implementation follows the [efficiency design](../../architecture/fleet-efficiency.md).

One logical road uses an ordered chunk manifest. Current/reference geometry can
share coordinate ranges, while original leg distance and duration remain part of
geometry identity. Mutable progress and fuel state no longer embed those arrays.
Changed intervals append compact receipts. Historical reads replay receipts and
load their immutable chunks; they do not recreate full snapshots for each change.

Ordinary deviation calculation connects to the next mandatory stop and reuses
compatible later legs. Stored chunk boundaries do not create separate provider
requests. No speculative internal-road reconnect search was added. Exact original
points remain available for local matching, with a shared spatial block index;
lossy display simplification does not determine calculation distances.

Bounded movement capture uses received GPS, not recurring provider history reads.
It keeps measured estimates, observed deviation chunks and explicit gaps. It
persists the checkpoint atomically with closed chunks, pins stop/truck/road
boundaries and ignores duplicate/older observations. It is not exact actual-road
or financial evidence. Missing observations stay missing. On-demand history is
bounded, company scoped and reads closed/open records in one database statement.
Normal map projections omit this history and keep existing geometry acknowledgements.

Existing leased planning queues, coalescing and provider budgets remain in use.
Cache entry budgets total 80 MiB: shared reads 16, display 16, exact indexes 32,
fuel 8 and requested raw history 8. Other caches and in-flight/runtime memory are
outside this total. Exact-index identity also includes the manifest, so a rolled
back revision cannot later reuse another road's cached index.

## Reproducible measurements

The local probe uses 100 independent synthetic roads, each with a 4,000 km leg
summary. The two densities contain 8,001 and 40,001 original vertices. These are
nominal 500 m and 100 m sampling cases, not real provider point-count forecasts.
Curvature and latitude vary; no routing-provider request is made by this probe.

The comparison uses the former exact linear matcher retained under test Support
and the current exact shared block index. Both return matching progress/distance
results on the sampled points. Each latency distribution contains 500 queries.
Timing is diagnostic, not a unit-test performance threshold.

| Measurement | 8,001 points: old → new | 40,001 points: old → new |
| --- | --- | --- |
| Build allocations, 100 indexes | 38,409,712 → 16,443,272 B | 192,012,680 → 78,444,536 B |
| Cold construction, 100 indexes | 85.95 → 150.21 ms | 473.23 → 744.42 ms |
| Warm matching p50 | 0.6978 → 0.0396 ms | 3.4892 → 0.1528 ms |
| Warm matching p95 | 0.7125 → 0.0429 ms | 3.5657 → 0.1629 ms |
| Ten full-plan read allocations | 24,425,880 → 10,264,576 B | 129,817,400 → 58,940,328 B |
| Ten full-plan reads | 39.83 → 19.38 ms | 347.99 → 114.32 ms |
| Active logical JSON payload | 844,052 → 235,140 B | 4,317,550 → 1,220,639 B |

Index construction allocates about 57–59% less, while warm matching p95 is about
17–22 times lower in this fixture. Cold construction is slower because it builds
spatial bounds: approximately 1.50 ms rather than 0.86 ms per 8,001-point index.
The cache reuses that work across progress updates. This does not mean every
operation became faster.

The logical payload comparison includes state, active manifest and coordinates;
it excludes historical receipts and database row/index overhead. Replacing one
vertex in the synthetic detour adds one new coordinate chunk of 22–23 JSON bytes.
That is the coordinate payload only, not the total transaction size or the size
of a realistic multi-point detour.

Managed live-memory deltas after collection were also recorded: for 100 indexes,
38,425,800 → 20,104,560 B at the lower density and
192,059,808 → 78,489,824 B at the higher density. These process-wide GC deltas
contain noise from other managed activity; the allocation measurements above are
the stronger comparison. Neither is container RSS. The dense probe deliberately
holds all 100 indexes; production cache admission/eviction still uses its fixed
budget and may trade residency for reconstruction work.

## PostgreSQL values and write volume

A separate probe used only the existing isolated PostgreSQL fixture and a
synthetic 8,001-point road. It compared `pg_column_size` values after server-side
storage/compression, including the new initial change receipt:

| Measurement | Old | New |
| --- | ---: | ---: |
| Stored column values | 272,905 B | 145,143 B |
| Logical state payload across 20 progress writes | 16,882,035 B | 29,075 B |
| New geometry chunks during those 20 updates | — | 0 |

Stored column values decreased approximately 47%. Progress-write payload
fell approximately 99.83%, or 581 times. Column sizes exclude heap pages,
indexes, TOAST indexes and WAL; write-payload bytes are not network-traffic or WAL
measurements. Do not extrapolate these percentages to the whole database.

## Validation and migration boundary

Regression coverage includes lossless coordinate restoration, preserved leg
measures, shared current/reference chunks, partial replacement and return to an
old road, stale-write rejection, historical reconstruction, unchanged-state
writes, checkpoint restart/idempotency, gaps, reverse/overlapping roads, immutable
cached ownership, and reuse of mandatory-stop suffixes without another full route.
The history query is exercised on SQLite and PostgreSQL.

Prepared additive migrations:

- `20260921203755_StoreRouteChunks`
- `20260921204626_RecordRouteMovement`

The PostgreSQL fixture exercised upgrades and refusal to downgrade over referenced
geometry or recorded movement. Legacy inline plans remain readable and convert
on their next successful write. Existing application rows have not been converted
or migrated as part of this local work. No automatic historical purge was added.

Several intermediate full runs passed. Some other runs encountered connection
timeouts in the remote PostgreSQL fixture; these were not hidden, skipped or
replaced with tests against an application database.

Final `bash test.sh all` passed: 3,138 Server tests, 1,052 Client tests and
625 JavaScript tests, with no failures or skips (4,815 total). Evidence:
`artifacts/managed/diagnostic-ybSJe5/tests.log`, pinned locally with `.keep`.
The pinned CSharpier check passed for all 69 changed maintained C# files;
`git diff --check` also passed.

Measurement evidence: `artifacts/managed/diagnostic-Bt8Z6r/measurements.log`
(pinned locally with `.keep`). The three measurement tests passed. Sources:
[allocation probe](../../../Server.Tests/Routing/RouteFleetAllocationTests.cs)
and [PostgreSQL probe](../../../Server.Tests/Persistence/RouteChunkSizeTests.cs).

No live 100-truck/provider test, browser interaction run, production heap capture,
container RSS benchmark, provider billing measurement or queue-age load benchmark
was performed. Passing correctness checks and these synthetic figures are not a
production capacity guarantee. The existing 512 MiB deployment target is unchanged.
