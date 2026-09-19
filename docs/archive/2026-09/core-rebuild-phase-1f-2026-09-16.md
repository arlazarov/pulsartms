# Core rebuild phase 1F: shared inputs for preview and live planning

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Delivered behavior

Saved truck/fleet previews and live planning now select remaining work from
TruckItinerarySnapshot through TruckPlanningInputsReader. Per-truck reads no
longer query the Dispatch Board or independently resolve mutable assignments.
Fleet/board reads retain paging to enumerate requested truck IDs; the displayed
load list cannot choose the current load or hide stored current work.

The reader captures the complete itinerary and selected driver external
identity in one database read scope. It reuses existing bounded display caching
and UTC-date/board/dispatch/execution invalidation. Cold saved preview retains
its six-query fixture budget; warm preview and repeated planning polls retain
zero-query checks. These are test-fixture counts, not production benchmarks.
Live planning requests HOS once per batch after capture finishes. It attaches
clocks to the captured driver; an active native assignment without a driver
cannot inherit the old truck driver's clocks. Preview never requests HOS.

PlanningWorkPolicy makes the existing display subset explicit while the
captured itinerary retains planned and overdue work. Invalid native current
work blocks selection instead of falling through to a later legacy load.
Existing saved owner, stop, assignment revision and input/profile checks remain;
missing or stale geometry does not skip the first remaining load.

ExecutionRouteProjection replaces the ETA-specific compatibility projection
and is shared by ETA and planning reads. It is a pure adapter from captured
facts to the Dispatch input still required by route algorithms. Commodity and
notes now travel with immutable visit facts; a metadata update does not require
replacing unchanged geometry. The internal itinerary signature policy advances
to version 3. Public HTTP and Client contracts are unchanged.

## Limits and remaining migration

Display inputs retain bounded cache freshness. They are not a fresh validation
of calculation publication and do not provide cross-process invalidation. ETA
continues to read fresh snapshots. HOS and existing settings/profile caches are
not part of the operational database snapshot transaction.

Explicit per-dispatch reads still resolve the requested assignment initially.
Captured facts replace it when that work is present; completed/historical legs
outside remaining work retain their compatibility path. Fuel assignment checks
still use a Dispatch DTO projection from captured candidates. This stage does
not migrate AutomaticPlanningService writes, RouteChoiceService, FuelHorizon
or the historical input owner. Existing refresh scheduling remains unchanged.

## Allocation-test diagnosis

The prior phase's full-suite failure was reproduced with temporary per-call
allocation counters. One match measured 5,648 bytes while the other 39 measured
32 bytes each: 6,896 total, with a collection during the measured region.

A controlled isolated run forcing a collection still passed at 1,280 bytes.
A separate concurrent allocation-pressure experiment reproduced the failure:
8,648 bytes, with one match contributing 7,400 and the other 39 contributing
32 each. The experiment allocated temporary arrays on another task and recorded
34 collections. Its final version awaited that task outside the measured loop.
This establishes interference from concurrent allocation pressure; it does not
identify the exact runtime allocation responsible for the extra bytes.

FuelSearchGeometryAllocationTests now belongs to the non-parallel
`Allocation measurements` collection. Its original measured loop, warmup and
4,096-byte match/180,000-byte index limits are unchanged. Other test collections
retain parallel execution. No fuel production algorithm was changed, no check
was removed, and all temporary instrumentation was removed.

## Verification

Added 14 server cases covering captured driver/HOS identity, assignment changes
during an HOS request, cache invalidation, complete batch scope, cancellation,
screen-independent current work, malformed/source-review roots and metadata
refresh with unchanged geometry. An architecture guard prevents preview/live
truck reads from returning to board-selected work or mutable resolution.

Affected categories `bash test.sh dispatch routing fuel synchronization` passed:

- Server.Tests: 2,038 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed across synchronization and architecture suites.

Required full `bash test.sh all` passed:

- Server.Tests: 2,261 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,834; no failures or skipped tests.

The full run includes both allocation cases, route-preview cold/warm budgets,
planning polling checks and both architecture suites. The compiler and pinned
CSharpier checks passed for the changed source. Documentation links and scoped
whitespace checks passed. The pre-edit hash baseline preserves unrelated
worktree changes; the ETA projection removal is its move to Execution.

Early iterations exposed two fixture namespace/reference mistakes, duplicate
empty truck unit numbers and an unnecessary empty-driver query. The fixtures
were corrected and the empty query eliminated. No query budget, assertion or
architecture rule was relaxed.

Managed local evidence is pinned in `artifacts/managed/diagnostic-UhTXMd`.
Controlled allocation experiments are pinned in `diagnostic-EoWNdw`,
`diagnostic-zAYPfW` and `diagnostic-3moef5` under `artifacts/managed`.

No suitable isolated PostgreSQL fixture was available. PostgreSQL execution and
isolation, production performance, browser behavior and release checks were not
run. No Client UI was changed. No migrations were authored or applied, and
nothing was deployed.

## Next bounded slice

Migrate fuel-horizon inputs and assignment validation to the complete itinerary,
with explicit overdue/native scope and preserved saved-route identities. Then
migrate planning writes and route choices, and consolidate historical inputs
before removing compatibility readers. Normalized visit storage, native
successor forecasting and financial evidence remain separate work.

See the [core specification][spec] and [acceptance scenarios][scenarios].

[spec]: ../../architecture/core-rebuild.md
[scenarios]: ../../architecture/core-rebuild-scenarios.md
