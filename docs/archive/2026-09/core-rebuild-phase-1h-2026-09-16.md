# Core rebuild phase 1H: captured work for route mutations

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Delivered behavior

Automatic planning now selects current and upcoming work from a fresh complete
TruckItinerarySnapshot. It no longer requests Dispatch Board rows to decide
which assignment to calculate. The existing planning subset, ordering,
completion progression, native receipt rules and provider backoff remain.
HOS clocks are associated with the captured driver after the read transaction.

Per-dispatch and upcoming preparation use the initial lookup only to locate a
truck and leg. Captured facts replace the mutable assignment. The selected
truck remains the gate owner, and the same capture reaches route building and
automatic progress. Native leg identity survives an implicit initial lookup.
Manual route builds also capture fresh work before entering their write path.

Route building and tracking verify fresh work under the truck gate. After
provider work, they compare the complete signature again before saving.
Base-road preparation called by route building checks that same capture before
its own write. Tracking also rejects a saved road whose owner or input hash no
longer matches the captured assignment. Detected changes preserve the previous
saved live route; a failed new build cannot publish its base or live road.

Route-choice preview and save use the captured queue for current-load selection.
The server draft stores the complete input signature and its as-of instant in
existing JSON storage. A change during provider calculation cannot replace the
previous draft. Save rereads work at that instant and checks the signature,
without depending on a cache invalidation notification. Existing actor, expiry,
choice revision, profile, GPS movement, remaining-stop and live-plan version
checks remain. Older drafts without the stamp require a new preview.

TruckPlanningInputsReader shares capture and driver mapping between bounded
display reads and fresh mutation reads. Fresh capture rejects an older caller
transaction. Calculation and provider calls run after the read transaction.
No new business table, schema migration, public HTTP field or Client change was
introduced. Financial formulas and route geometry policies were not changed.

## Verification

Added 15 server cases covering:

- Assignment, completion revision, queue and configuration changes during
  routing; neither base nor live road is published.
- Changed inputs during rerouting preserve the previous saved plan.
- Progress refuses stale geometry without requesting a replacement route.
- Automatic selection bypasses a warm work cache without invalidation and
  makes no Dispatch Board request.
- A changed preview calculation retains the previous durable draft.
- Queue, configuration and actual-event changes reject a choice save without
  replacing the prior saved choice or making another routing call.
- Unstamped drafts fail closed, and native revision changes invalidate a draft.
- Native builds retain the resolved leg when the caller omits it initially.
- Fresh work bypasses display caching and rejects an existing transaction.
- Architecture guards prevent Board selection and mutable assignment reloads
  from returning to the migrated paths.

The existing synchronization regression still asserts zero database queries for
warm display polling. Its progress portion now asserts the exact cost of two
fresh captures on an unchanged progress operation, rather than incorrectly
requiring a mutation path to trust cached work. Provider calls remain forbidden
in that fixture. This is a query-budget check, not a production latency result.

Integration required registering the real execution read scope in the pipeline
fixture and wiring the shared reader into base-route fixtures. Two older route
fixtures now specify their assigned status instead of relying on an empty
status. The new active native preview fixture supplies fresh GPS, retaining the
production freshness requirement. No architecture exception or assertion was
removed to accommodate the migration.

Affected categories `bash test.sh routing dispatch fuel synchronization` passed:

- Server.Tests: 2,065 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed across synchronization and architecture suites.

Required full `bash test.sh all` passed:

- Server.Tests: 2,288 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,861; no failures or skipped tests.

The pinned CSharpier check, scoped whitespace and local documentation links
passed. Managed logs and the pre-edit hash baseline are pinned under
`artifacts/managed/diagnostic-195OTI`. The stage changes 28 source, test and
documentation files; pre-existing work is retained.

## Remaining boundaries

- Complete signature checks precede existing result transactions. The native
  leg locks and optimistic result checks remain, but there is still a
  cross-process interval between source validation and result publication.
  This stage does not claim an atomic input-to-result publication contract.
- Initial per-dispatch lookup retains its compatibility cache. A stale locator
  can fail closed until refreshed; it cannot supply the calculation's facts.
- Profile/settings caches, telemetry and schedule inputs retain their separate
  policies. Complete work validation does not make those inputs transactional.
- Standalone unassigned/historical base roads and deadhead predecessor reads
  retain their separate input paths. General profile writes and fuel
  compatibility writers still need consolidation; fuel calculations retain
  their phase 1G validation.
- Complete signature matching can conservatively reject a result after a
  change that does not alter geometry. Geometry reuse remains separately keyed.
- Ordering and transfer semantics were preserved. The migration does not prove
  physical precedence for ambiguous work or enable native successor forecasts.

No suitable isolated PostgreSQL fixture was available. PostgreSQL execution,
live provider behavior, authenticated browser checks and production performance
were not tested. SQLite and automated suites do not establish those results.

Next: consolidate the remaining historical/profile/compatibility inputs and
define atomic publication across source changes, before removing old bridges
or using planning captures as accounting or driver-settlement evidence.
