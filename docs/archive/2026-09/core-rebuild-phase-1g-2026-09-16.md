# Core rebuild phase 1G: fuel consumes captured truck work

Date: 2026-09-16. Status: implemented locally; full automated suite passed. No
deployment, migration or application-database experiment was performed.

## Delivered behavior

Fuel calculations, edit previews/saves, saved-plan validation and automatic
refresh eligibility now use TruckItinerarySnapshot through
IFuelWorkInputsReader. Their assignment source no longer depends on Dispatch
Board rows. Horizon and terminal-access builders accept captured inputs rather
than caller-supplied screen load lists and no longer reload mutable
assignments during assembly.

FuelWorkInputs retains the full immutable itinerary and applies the existing
fuel subset explicitly. Assigned/in-transit legacy work includes overdue work;
legacy planned work remains in the capture but outside fuel scope. Existing
native delivery/transfer boundaries, receipt requirements and duplicate-load
rules remain. Selected unresolved work cannot produce purchase
recommendations.

Automatic search and manual editing resolve their requested root from fresh
captured facts before reading the current saved route. Horizon geometry and
terminal policy then share that capture. Existing confirmed-location, saved
road hash, profile, anchor, stop-count and geometry-size checks remain in
force. No routing/geocoding request was added, and fuel economics were not
changed.

Display validation reuses the bounded work cache without HOS.
PlanningReadService passes its existing itinerary to TruckFuelPlans, keeping
current selection and fuel checks on the same capture. FuelPriceRefreshService
uses this owner to check assignments before its existing price/recalculation
workflow. Actual calculation and final validation always request fresh work.

Fresh capture uses IExecutionReadScope and rejects an older caller
transaction. The capture transaction finishes before price reads, HOS schedule
evaluation or writes. Before publication, both automatic and manual paths
reread the complete itinerary, compare its content signature, and recheck
included assignments and stops. A changed input leaves both previous saved
fuel copies intact, including when no cache invalidation notification was
emitted.

## Compatibility and limits

Persisted fuel signatures and FuelPlanProjection retain their pure Dispatch
DTO compatibility format. Native and legacy signature parity is covered
directly; the adapter creates route inputs from captured facts through the
existing ExecutionRouteProjection. No new business table, migration,
historical snapshot storage, public HTTP contract or Client change was
introduced.

The remaining compatibility paths are explicit:

- The initial per-dispatch lookup locates the requested truck/leg. Captured
  facts replace its route input before calculation. General route-write
  orchestration, gate ownership and profile/store helpers remain separate work.
- Legacy deadhead predecessor selection still reads historical evidence outside
  the itinerary capture. Saved-road hash/anchor validation remains separate.
- Profile/settings, telemetry and schedule inputs retain their own policies;
  they are not a transaction over the whole calculation lifetime.
- Final itinerary validation precedes the existing result transaction. Expected
  result revisions and atomic replacement remain, but this does not close every
  cross-process source read-to-write race.
- Fuel keeps its current ordering/transfer policy. This stage does not prove
  physical precedence for ambiguous work or enable native successor forecasts.
- Bounded display cache freshness and per-instance invalidation remain. A
  normalized number-only assignment may conservatively invalidate an older
  signature; mismatches are never ignored to preserve a recommendation.

The complete input signature is an in-memory calculation check, not durable
accounting or driver-settlement evidence.

## Verification

Added 12 server cases, including the architecture guard:

- Native/legacy signature parity against existing authoritative board output.
- Fresh capture bypasses a warm display cache without invalidation.
- Horizon assembly retains captured visits after stored metadata changes.
- Overdue assigned work is included despite screen filters; planned legacy
  work remains outside the fuel subset.
- Malformed and source-review-blocked native work fails closed.
- Fresh capture rejects an existing database transaction.
- Automatic and manual saves reject input changes during price reads and
  preserve both saved copies.
- Fuel consumers cannot return to board queries or assignment reloads in the
  horizon and terminal builders.

Affected categories `bash test.sh fuel routing dispatch synchronization`
passed:

- Server.Tests: 2,050 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed across synchronization and architecture suites.

Required full `bash test.sh all` passed:

- Server.Tests: 2,273 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,846; no failures or skipped tests.

Existing checks cover native-to-legacy continuation, completed-load
progression, provider-free saved roads, quantity editing, compare-and-swap
replacement, polling/query budgets, allocation bounds and architecture rules.
The pinned CSharpier check, scoped whitespace and local documentation links
passed.

Initial integration exposed three fixture assumptions: duplicate empty stop
IDs, raw unnormalized operation fields in a saved signature, and missing input
reader registration in a worker fixture. These were corrected without relaxing
assertions or production validation. The old impossible legacy
execution-revision fixture now expresses the mismatch in the requested
revision.

Managed evidence and the pre-edit hash baseline are pinned under
`artifacts/managed/diagnostic-29lR7c`. The baseline confirms only this stage's
intended source, test and documentation changes; existing work is preserved.

No suitable isolated PostgreSQL fixture was available. PostgreSQL execution
and isolation, production performance, browser behavior and release checks
were not run. SQLite tests do not prove PostgreSQL behavior or distributed
atomicity. No migrations were authored or applied, and nothing was deployed.

## Next bounded slice

Inventory and migrate AutomaticPlanningService and RouteChoiceService work
selection and mutation orchestration. Preserve captured identity through the
truck gate and the result writer, then consolidate historical road inputs and
cross-process source revision guards. Remove the remaining compatibility
readers only after their callers and durable formats have an explicit
replacement.

See the [core specification][spec] and [acceptance scenarios][scenarios].

[spec]: ../../architecture/core-rebuild.md
[scenarios]: ../../architecture/core-rebuild-scenarios.md
