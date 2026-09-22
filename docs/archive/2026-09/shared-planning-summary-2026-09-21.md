# Shared background planning display

Dispatch planning summaries and the selected-truck map planning endpoint now use
one company/truck cache. Explicit reads of non-current dispatches have separate
keys. This replaces synchronous per-truck fuel validation inside board requests.
The old board-only fuel prefetch contract and scoped memo were removed.

PlanningSummaryOperation has two consumers. Requests register bounded demand;
consumers refresh active entries about every 30 seconds, with a 60-second job
timeout. Demand expires after two minutes without readers. Current assignment and
settings signatures protect publication and reads. Failed refreshes retain prior
results with IsRefreshing; a changed assignment cannot reuse them. Entries and
payload bytes are bounded, including queued work, and exposed in memory diagnostics.

Each entry stores compact metadata and separately compressed display geometry.
Dispatch never decompresses geometry. Map callers receive geometry only when
their known plan/version differs. Returned models do not share mutable instances.
Cold browser responses retain prior map data only if all assignment identities
match. A fresh browser can show an updating state while the cache rebuilds.

Existing database publication remains authoritative. No periodic display-row
write, new table or migration was introduced. Restart recovery uses existing saved
route/fuel records asynchronously. API instances must run PlanningSummary locally;
this cache is per process, not distributed. See the architecture guide for limits.

## Local evidence

- API build: `artifacts/managed/diagnostic-so13t8`.
- Client build: `artifacts/managed/diagnostic-6Wu7ny`, zero warnings/errors.
- Real fixture HTTP: `artifacts/managed/diagnostic-WXkkRI`.
  On the existing 50-truck fixture, the first 12-truck summary request took
  794 ms and returned pending entries. Background readiness progressed 0, 4,
  6, 8, 10, 12 over approximately 24 seconds. Warm summary requests took
  119–192 ms. The map read of the first ready truck took 174 ms. The last board
  and map HTTP scopes recorded no SQL commands. These figures exclude the
  asynchronous calculation cost and are not whole-page rendering timings.
- Memory: `artifacts/managed/diagnostic-gy4xbm`: 12 entries, 4,124,128 payload
  bytes against an 8,388,608-byte limit. This includes compressed display geometry,
  not just compact metadata, and excludes dictionary/object overhead.
- The initial uncompressed implementation rejected long-route payloads above its
  entry limit. It was replaced before completion; the successful observations
  above are from the compressed map/metadata split. The measurement script's final
  memory extraction used an incorrect response wrapper and failed after saving
  request and SQL evidence; memory was then collected separately.
- Full `bash test.sh all`: 3,186 server, 1,054 Client C#, 625 JavaScript tests
  passed, plus TypeScript checks (`artifacts/managed/diagnostic-yaMUcm`).
  Query-count and cache-inventory assertions reflect the new implementation.
- Browser smoke: refreshed Dispatch displayed distances and fuel-stop counts;
  selected-truck map navigation was also exercised. Fixture ETA expiry remains
  independent of summary readiness.

No production deployment or migration was performed. Cold initialization is
asynchronous, and cache eviction can require another background reconstruction.

The additional map/metadata parity regression and the other four cache tests
passed in `artifacts/managed/diagnostic-HNJWaw` after the full suite. Browser
verification confirmed the selected TEST-001 map drew its route and displayed
1,182 miles to the next stop, matching Dispatch. No new production code changed
after the successful full suite.

## Committed updates and prepared-result publication

Route, profile, progress and fuel publication now notify existing display demand
through PlanningWorkPublication after successful commit and read invalidation.
Cache tickets reject stale in-flight work, including another writer's commit.
A failed commit cannot trigger a display update. Existing display data survives
while its replacement is being prepared. Notifications allocate no new truck
entries.

The planning worker can publish its prepared route through
PlanningSummaryPublisher, with authoritative fuel projection and cached ETA,
instead of reloading route geometry. Display serialization does not mutate the
calculation. Completed routes are left to itinerary selection in the recovery
reader. A newer commit, changed signature or evicted entry prevents publication.
Periodic background validation remains for cold demand, external telemetry/ETA
freshness and cross-process recovery; not every refresh has been eliminated.

Local evidence:

- Final API build: `artifacts/managed/diagnostic-hkN73G`; final local restart:
  `artifacts/managed/diagnostic-EGt6tI`. Existing isolated fixture, no schema change.
- Three warm map API calls: 115.7, 28.3 and 27.1 ms, all with ready state
  (`artifacts/managed/diagnostic-CD3XUR`). These are individual request timings,
  not a controlled throughput or process-memory comparison.
- Commit refresh exercise: `artifacts/managed/diagnostic-ofpcuY`. Immediately after
  a fresh snapshot, synthetic telemetry was advanced and truck 0 was refreshed.
  The previous snapshot remained present while updating; a new ready snapshot
  arrived 7.54 seconds after starting the exercise, before the 30-second fallback.
  This covers commit-driven reconstruction, not a benchmark of direct prepared
  result publication. The preceding measurement attempt failed parsing .NET's
  seven-digit fractional timestamp before performing the update; the parser was
  corrected for this successful run.
- Browser: Dispatch displayed distances and fuel-stop counts; navigating its
  TEST-001 map link displayed the shared 1,182-mile next-stop distance.
- Regression coverage includes failed commit, notification ordering, stale
  publication rejection, company isolation and preservation of calculation data.

No production deployment, migration or additional memory-limit increase.

Final verification: `bash test.sh all` passed 3,191 server tests, 1,054 Client
C# tests and 625 JavaScript tests, plus TypeScript checks, in
`artifacts/managed/diagnostic-njrnNl`. Architectural checks were retained unchanged.
`git diff --check` passed. The final localhost API was restarted and the selected
truck map was reloaded successfully. PostgreSQL transaction-failure regression
was not rerun against PostgreSQL; the new commit regression uses isolated SQLite,
while the local HTTP smoke uses the existing isolated remote PostgreSQL fixture.
No new performance comparison of total CPU or memory was performed in this step.
