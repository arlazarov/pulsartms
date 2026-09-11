# Code and performance audit

## Scope and evidence

Reviewed the recent routing, deadhead, financial snapshot and Dispatch UI changes,
their background callers, dependency registration and existing layer/style/JS
regression checks. Repository-wide source checks cover layer references, provider
SQL in Application, maintained documentation, comments and style token contracts.
This is a static audit with automated regression tests, not a production load test
or a new browser heap/visual certification. No paid route requests were needed.

## Corrected

- Moved PostgreSQL locks from route budgeting and fuel import to Infrastructure,
  exposed through `IAppDbContext`. Transactions retain their existing boundaries.
- Made route budget and fuel horizon required DI dependencies; removed fallbacks
  that bypassed registration and could omit saved-route reuse.
- Moved financial snapshot persistence to a scoped Dispatch service. A transaction
  lock serializes snapshot upserts across PostgreSQL processes.
- Dispatch reads validate saved financial inputs against the current connection.
  A changed price uses the shared server formula without another route request.
  Client receives RPM values and only formats them.
- Internal planning and synchronization board reads skip financial enrichment.
  This avoids the history, deadhead, rate and per-truck profile queries entirely
  when their results are not consumed. Public board reads retain enrichment.
- Indexed loaded history by dispatch and truck in memory instead of scanning every
  truck's history for every card. Predecessor selection is unchanged.
- History queries now project only connection, scheduling and financial fields;
  cargo, notes and other unused stop data are not fetched.
- Fuel horizon requires saved base-route and deadhead services. Route preview
  requires its read/display caches; nullable bypass paths were removed.
- Geocoding failures are shared in process for their retry window (at most one
  hour), without retaining provider response bodies. Cancellation is not cached.
- JavaScript build ownership is explicit in Cloud Build: the Node step builds
  assets and the .NET step consumes them. Client deployment starts with a clean
  publish directory and preserves the previous output in a temporary backup.
- Removed the unused alternative fuel recommendation implementation and its
  provider dependency. The active fuel planning path remains unchanged.
- Standardized Dispatch card typography, spacing and radii with existing tokens;
  added a reusable pill radius. SVG geometry and animation dimensions remain exact.

## Remaining risks, ordered by impact

1. **History growth:** `DeadheadService` still loads all non-cancelled history and
   stop projections for the requested trucks. Projection reduces row width, not
   row count. Introduce a compact ordered connection index or a validated
   predecessor query before fleet/history growth. Arbitrary date cutoffs are unsafe.
2. **Provider retries:** temporary routing failures retry after five minutes;
   geocoding caches results and failures in process memory only. Multiple distinct
   requests and process restarts can still cost money. Persisted routing budgets
   help but do not constitute a Google geocoding budget. Measure counts separately
   by provider and operation before selecting additional limits/backoff.
3. **Global serialization:** route builds, deadhead preparation and geocoding use
   process-wide gates; database locks serialize budget and financial writes. This
   is conservative for correctness but can create head-of-line blocking. Measure
   queue wait separately from provider duration before introducing keyed concurrency.
4. **Background work:** the base-route worker resolves profiles separately for base
   and deadhead operations, and offset paging scans the eligible queue repeatedly.
   A dirty-work queue with input versions would reduce unchanged polling; it must
   retain retries and handle insertion/reassignment during processing.
5. **Retention:** reroute attempts accumulate. Any cleanup must preserve the last
   position per truck as well as the rolling 24-hour window used by the budget.
6. **Ambiguous history:** an undated load in a truck's history can make predecessor
   selection unavailable. This deliberately avoids invented empty miles but needs
   an explicit data-quality status and operator correction workflow.
7. **Address-provider limits:** confirmed addresses are persisted per stop, with
   source preservation and expiration. This does not replace a separate global
   Google request budget; the truck-routing budget covers a different provider.
8. **Runtime validation:** existing JS tests exercise bounded playback, layer reuse,
   teardown and route selection, but cannot establish absence of real-browser memory
   leaks. Repeat authenticated navigation/heap and desktop/mobile visual checks
   against a coherently rebuilt Client/API before deployment.

## Enforcement and rollout

`LayerBoundaryTests` rejects provider SQL in Application and optional registered
dependencies in public Application services and handlers. JS style tests reject raw colors and invalid token usage; the Dispatch
card test also rejects raw spacing, typography and corner dimensions. Existing CI
runs these tests before image publication. `AGENTS.md` records mandatory completion
checks. These guards complement review; they do not prove every architectural rule.

The `StoreDispatchRates` migration was applied to the existing shared database on
2026-09-07; no separate database was created. Local API and Client were rebuilt and
restarted. Automated checks passed: 333 server tests, 57 JavaScript/style tests and
the Client warning-as-error build.

Authenticated browser checks confirmed Dispatch financial fields, Papers popup,
map startup, and Follow resuming at zoom 15 after manual zoom-out. Mobile details
were checked at a 390-pixel viewport with no document horizontal overflow. Camera
opened with its last available image; fresh capture was not certified. The visible
Chrome lifecycle probe completed 12 map/Dispatch cycles without browser errors;
map canvases were removed on navigation. Post-GC JS heap growth between its baseline
and final three-sample windows was 1.58 MiB. This is not a zero-leak claim or a GPU
memory measurement. A longer soak and production lock-wait measurements remain
outstanding.

The CPU-only layer preparation probe completed seven samples of 30,000 frames
(three trucks, 1,000 stations and a 2,000-point route). Median preparation time was
98.19 ms with caches versus 283.65 ms with its cache-disabled control. This is an
ablation result, not a historical before/after comparison, FPS measurement, or
evidence about network latency or GPU memory.

The address follow-up below supersedes the previous ambiguous-address failures
for loads 1358, 1370 and 1373.

API rollout completed as `amftms-api-00071-pjh` (100% traffic), followed by Firebase
Hosting publication of the clean Client output. The production liveness endpoint
returned HTTP 200. Cloud Build also passed the 333 server and 57 client tests.

## Address and dependency follow-up

All registered dependencies in public Application services and handlers are now
required; test fixtures supply the same caches/options instead of exercising null
bypass paths. Gate waits for route builds, base routes, deadheads and budget checks
are recorded separately in request diagnostics. Production lock-wait measurements
and a longer authenticated browser soak remain outstanding.

`StoreVerifiedStopAddresses` was applied to the existing shared database. The API
was deployed as `amftms-api-00072-6gq`, with 100% traffic and HTTP 200 liveness.
Cloud Build `f1b26d29-82c1-4f42-a52d-d8716de33df0` passed 341 server tests,
57 client tests and the Client build. An additional local concurrent-import
regression brings the local server suite to 342 tests. Client contracts and assets
were unchanged for address persistence.

Live Google checks resolved the Pageland, Laredo and Fort Mill problem addresses.
Database reads confirmed corrected components and preserved source snapshots on
loads 1358, 1369, 1370 and 1375. During the rolling transition the old synchronization
overwrote 17 corrected addresses; after the old revision stopped serving, only
those identified verification flags were reset. Background verification restored
the corrected values under the new synchronization logic.

Deadhead distances are now present for 1358 (78.021 mi) and 1373 (42.573 mi).
Load 1370 initially remained blocked by an unsupported-road section; the policy
follow-up below resolves its missing financial distance. See
verified-stop-addresses.md for correction ownership, invalidation and retention.

## Route-section policy follow-up

At the user's request, section restrictions now produce persisted warnings rather
than rejecting mileage. Provider geometry and distances are retained unchanged.
Current-route Map and Dispatch planning views display access warnings. Invalid
responses and daily/minute request limits remain enforced.

API revision `amftms-api-00073-qbb` serves 100% of traffic; Firebase Client was
published as well. Local and cloud checks passed 342 server tests, 57 client
tests and the Client build. Liveness returned HTTP 200.

After rollout, two identified cached section failures and the single failed
deadhead retry for 1370 were expired without deleting audit rows or resetting
budgets. Background preparation persisted 160.819 empty miles, loaded RPM
3.133531 and total RPM 2.886951, with the access warning retained in route JSON.
This was confirmed by a database read; a new visual browser certification was not
performed for the warning text.
