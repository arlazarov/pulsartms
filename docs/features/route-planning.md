# Truck route planning

## Operational road and display context

`RoutePlan.Route` remains the operational road used by ETA and fuel.
`ReferenceRoute` supplies optional full-route display context. Reconstruction
failure retains the previous reference and does not discard a valid operational
road. User cancellation still cancels the operation. Reference geometry and
movement segments explicitly identify their source as `EstimatedRoad`; neither
is recorded GPS travel or an accounting mileage fact.

Application classifies each segment's cargo state and purpose. Fleet Map and
Dispatch consume that classification and select colors only. An unknown state
remains unknown. The initial approach to the first pickup is empty; a later
pickup uses the preceding accepted stop's state, including partially loaded work.

Planning input capture resolves current work using matching saved completion
proofs before selecting the effective native crew. An older active assignment
whose route is complete cannot supply HOS for the next planned assignment. A
native assignment with no driver does not inherit the truck catalog driver.
Completion and profile changes invalidate the cached context. Cached profile
reads occur before the work-cache factory to avoid recursive cache locks.

## Route choices and via points

Fleet Map offers Route options for the selected current load and an inspected
future load. Opening the editor explicitly requests up to three TomTom truck
routes; a provider may return fewer, and identical returned geometry is deduplicated.
All options retain truck dimensions, axle, weight and hazmat restrictions and
visit order. A current truck with a valid saved route and fresh GPS compares only
the remaining road from its captured GPS position, through unfinished truck stops.
The GPS origin is explicitly labeled, timestamped, and not persisted as a load stop.
Future loads still compare their own stop-to-stop routes. A started load without
fresh GPS or compatible saved geometry fails explicitly instead of silently using
the original pickup. Unstarted loads without a usable GPS/saved route retain the
full-itinerary preview. Imported/confirmed completion and the existing stop tracker
determine remaining visits; coordinates alone do not prove pickup completion.

Edit route adds a city/address or map point before a specified stop. Map diamonds
are draggable; road hovering exposes a reusable drag handle. Via points can be
removed or reordered within their leg, but cannot reorder cargo stops. The server
verifies every expanded endpoint and collapses via sublegs into the original
load-stop legs. Via points add driving distance/time, not service events. Limit:
20 via points and 50 provider locations including load stops.

The editor compares distance and driving duration with the saved road (or the
first option when no saved baseline exists). These durations are not HOS-aware
arrival times. Selecting a line/card only previews it; Use this route commits.
For a GPS preview the baseline is trimmed to the same remaining stops when the
truck can be matched to that road; an off-route position has no saved comparison.
Saved via points belonging to completed legs, or demonstrably behind GPS on the
current road, are omitted when reopening from the current position.
The preview response carries simplified display coordinates once per leg and
only saved distance/time comparison values. Exact geometry remains in the
server-owned draft and saved road. Selecting an existing option publishes only
selection metadata; it does not resend coordinates or recreate the map polylines.
The request contains a server preview ID, option number and expected revision,
never client-authored road geometry. Active Admin/Dispatch actors can calculate
and save. Drafts are shared across API instances, expire after ten minutes, and
retain at most one bounded 8 MiB draft per actor. A new preview removes expired
draft rows. Starting another preview replaces that actor's previous draft.

Saved choices are separate from imported dispatch fields. Preview and save use
fresh complete truck work, including queue membership, resource revisions and
visit facts. Draft JSON retains its input signature and as-of instant; old
unstamped drafts require a new preview. Changed work during provider calculation
cannot replace the previous draft. Saving compares that signature again without
relying on cache invalidation, alongside existing input/profile and revision
checks. Completion does not erase the choice; live
reconnection retains the selected road ahead. Changed itinerary geometry or
truck restrictions require review rather than silently replacing the choice.
ETA uses the selected road on refresh. Route-choice revisions also invalidate
the affected truck's saved fuel horizon, including choices on future loads;
fuel purchases still require explicit recalculation.

A current-road save retains the full baseline separately and replaces the live
remaining plan atomically with its choice revision. It does not rewrite imported
loaded mileage or fabricate a driven prefix. Reconnection uses the chosen remaining
road, not the old full baseline. The draft also binds the live plan ID/version and
remaining stop IDs; changed completion, route revision, stale GPS or movement over
two miles from the preview origin requires a new preview before saving.
This additive JSON representation requires no new database migration. Deploy the
API and Client together; older API builds do not understand remaining-road choices.

Storage requires `20260911222023_AddDispatchRouteChoices` and
`20260911224154_AddRouteChoicePreviews`. Apply migrations before running the new
API; deploy Client and API together. Migration generation does not apply them.
The alternative-route provider contract follows the
[TomTom Calculate Route documentation](https://docs.tomtom.com/routing-api/documentation/tomtom-maps/v1/calculate-route).

## Captured work for route mutations

Automatic planning selects current and upcoming work from a fresh complete
itinerary through the shared planning policy. Board screen filtering and cached
assignment data do not decide the selected work. Per-dispatch lookup only locates
the truck and leg; captured facts replace its route inputs. The same capture
remains attached to subsequent build/progress operations under the truck gate.
Manual route builds also capture fresh inputs. Provider calls run outside the
read transaction. A changed signature prevents publication of the calculated
road; progress updates also reject saved roads that no longer match their work.
Base-road writes invoked by route building validate the same capture first.

Display reads retain their bounded cache. Mutation checks intentionally reread
work, including on progress operations that make no write. The existing result
checks for native leg ownership and optimistic result revisions remain.
PlanningWorkPublication now validates canonical work inside the same protected
transaction that writes the result, including choice drafts and captured base
roads. A failed validation or commit cannot publish the replacement.

The PostgreSQL adapter protects canonical/historical work, stored settings and
saved roads with database-maintained truck revisions and a shared global guard.
Missing defaults, new queue membership, removed links and old/new assignments
participate through writer triggers. Publications for independent trucks can
progress concurrently; shared loads and global resource changes still coordinate.
A busy owner defers publication. Provider requests remain outside protection.
Production contention and throughput have not been measured.

Profile/settings display caches retain their bounded lifetime. Automatic
exchange-rate saves acquire global publication ownership; unrelated checkpoint
rows remain independent. ETA and fuel capture saved-road versions and validate
them before writing results. Telemetry and independently prepared road inputs
retain separate policies. The pending core migration installs this ownership
protocol alongside accepted execution storage.

Live route builds capture the effective profile before provider work and check
it uncached inside publication before writing the requested profile. Automatic
builds also reject an already-stale supplied profile before calling a provider.
Manual builds may intentionally save changed dimensions, but cannot overwrite
an intervening profile edit unnoticed. Automatic progress and rerouting validate
only routing inputs before result writes, reusing the existing route signature;
fuel-only changes do not reject those writes. Base preparation and route-choice
preview/save use the same uncached routing guard before provider work and again
inside their publication transaction. A stale cached profile cannot certify a
draft or selected road. A late dimension change preserves the previous draft,
choice, base and live plan. Route choices retain their existing input, work,
GPS and result-revision checks; fuel-only settings do not reject them.
Standalone base preparation protects settings under the same scope while
retaining its separate historical/unassigned work-input policy. See
[base routes](base-routes.md) for transaction ownership and remaining limits.

## Stop operations

Dispatch Details provides `Edit stop operation` per visit, with separate action
and after-stop state. Driver start means No truck; equipment collection can lead
to Bobtail, Empty, Loaded or Unknown as appropriate. Pickup means Loaded; delivery
requires an explicit choice between Empty, remaining Loaded cargo, and Unknown.
Dropping a trailer leaves Bobtail. Waypoint supports an existing truck movement
without claiming cargo pickup. Missing weight, commodity or trailer metadata does
not establish an empty or bobtail movement.

Manual operation fields are separate from imported Job/equipment/cargo data and
survive synchronization of the same stop identity. The authenticated Dispatch
write checks actor access, source-stop identity and optimistic operation revision.
Reset preserves a revision tombstone. Completion is independent: changing the
action does not mark an event completed or undo its actual time.

The truck itinerary excludes a confirmed personal prefix until the truck starting
anchor, imported truck assignment or explicit truck operation. Bobtail and empty
truck legs remain in route mileage, ETA and fuel planning. A discontinuous personal
leg inside truck driving is not silently connected; it requires a separate truck
assignment. Geometry signatures still depend on the included locations and truck
profile, not merely the action label. ETA/fuel inputs include effective operations.

Equipment operations do not inherit pickup/delivery service durations. Their
appointment waits conservatively consume duty time without rest credit. Personal
travel is not forecast as a rest period or as truck driving; existing driver/HOS
assignment checks remain in force. Unknown equipment service duration is not
invented. Fuel continues using the configured truck MPG and routing dimensions;
there are no measured bobtail/empty-specific MPG or weight profiles. Imported
financial loaded-mile values are retained, not recomputed from these states.

Storage requires migration `20260911202945_AddStopOperations`. Publish the API and
Client together after applying it. Creating the migration does not apply it.

## Display and ownership

Fleet Map and Dispatch display saved planning results. Application resolves the
current dispatch from authoritative truck assignments and stop progress; the
Client must not guess that identity from an ordered list of future loads.
A missing route does not make the current load a future load.

Saved preview and live planning read the same complete truck itinerary through
TruckPlanningInputsReader. Board paging selects trucks only; filtered screen
loads cannot choose current work. Explicit display policy preserves the existing
eligible work subset, while the captured itinerary retains planned and overdue
assignments. Unresolved assignment/source/visit problems block selection rather
than silently choosing a later load.

Captured work and its driver identity reuse the existing bounded cache
lifetime and board/dispatch/execution invalidation. Live planning requests
current HOS separately for that captured driver. ETA reads the assigned driver
for both planned and active native work, matching route eligibility. The truck
and assignment revision must match; completed or cancelled work cannot supply
current HOS. A native assignment without a driver does not fall back to the
previous truck driver's clocks.
These cached display inputs do not replace the fresh validation required
before publishing a calculation. Explicit historical per-dispatch reads retain
their separate assignment lookup until historical inputs are consolidated.

Saved base routes describe ordered load stops. Live plans add current progress,
tracking and rerouting. Delivery-to-next-pickup connections are stored separately:
truck movement does not invalidate those fixed endpoints. Changed assignments,
stops or vehicle profiles require signature validation before reuse.

The initial per-truck preview reads saved data without requesting HOS, geocoding,
fuel recommendations or provider routes. Normal planning then refreshes metadata.
Matching revisions omit geometry from HTTP and JS updates. Pending future routes
can request bounded background preparation without blocking the display.

See [base routes](base-routes.md), [empty mileage](dispatch-deadhead.md),
[synchronization](synchronization.md), and [architecture](../ARCHITECTURE.md)
for their owning policies.

## Progress and truck access

Stop progress must respect reported completion and verified tracking evidence.
Approaching a pickup does not mean it has been completed. Inferred plan progress
does not overwrite imported pickup/delivery timestamps. Rerouting uses remaining
mandatory stops and the configured GPS, deviation and request-budget guards.

Truck dimensions, weight, axles and applicable restrictions are server inputs.
Never fabricate tank capacity or MPG. A returned distance is not permission to
drive a road with truck restrictions. Preserve provider mileage and geometry while
displaying the [access warnings](route-access-warnings.md).
Address verification is governed by [verified stop addresses](verified-stop-addresses.md).

Per-leg durations are the canonical route time. Fresh and cached provider routes,
and saved routes assembled for fuel planning, reconcile independently rounded
summary seconds only within one second per leg. Larger inconsistencies fail
closed. Mileage and road geometry are unchanged; durable fuel snapshots still
require their total time to match the saved legs.

## Fuel is an explicit calculation

Fuel calculation reuses its initial assignment list, per-leg station matches and
request-local optimizer results. Stations outside the candidate radius are
rejected using geometric bounds before exact segment matching. These are work
reuse/pruning changes, not a coarser fuel route or relaxed reserve rule. The final
assignment/revision check before saving remains separate from the reused inputs.

In the fuel editor, the server prepares bounded five-gallon quantity choices for
the selected visit, with a 25-gallon minimum and an exact Full tank endpoint.
Increasing an earlier purchase reduces later purchases in order; decreasing it
increases later purchases where capacity permits. Later visits remain at least
25 gallons; unabsorbed surplus remains at the finish. Visits are never silently
removed. Reserve or capacity violations keep the draft invalid and block saving.
Automatic station selection retains its separate ten-gallon rounding policy.

Moving the slider copies the prepared balances and financial values without an
HTTP request. Selecting another visit or changing station/order loads a new
choice table; quantity controls wait for matching choices. Older API responses
without a table use the cancellable preview compatibility path. Saving always
replays the actual draft on the server using current inputs and the frozen saved
plan revision; prepared values are not trusted write data.

Opening or polling the map does not run a fuel-purchase search in the read
request. Background route refresh and fleet planning validate saved automatic
fuel plans against the updated road and measured fuel. Invalid automatic plans
are recalculated through the existing revision-guarded replacement command.
Manual purchases and manual starting fuel remain protected from replacement.
An initial plan still uses Calculate Fuel / Recalculate Fuel.

Recalculation keeps the previous display until a complete replacement is saved.
The requested truck profile, truck-owned plan and current-route compatibility
copy commit atomically. Profile changes are not saved during calculation.
Failure preserves the prior snapshot but a normal read must independently validate
its assignments, prices, settings, road position and fuel level before presenting
actionable purchases. Current route geometry remains separate from the wider
fuel-planning horizon, which can continue into the next assigned load.

An explicit profile save resolves the truck through fresh remaining work and
validates that capture inside the publication transaction. Completed work and
changed assignments cannot redirect a profile write. Profile cache invalidation
follows commit. This endpoint does not introduce a profile edit revision or
historical profile reconstruction; those require a separate contract.

The global Settings preference controls the calculation's IFTA basis; the map
toggle controls display. Reserve, fill target and hourly driving cost use the
server-owned fleet defaults, not hidden legacy Settings values. Other preferences
are unchanged, and effective profile signatures include these defaults.
Latest valid fuel percentage may be used as an estimate,
but missing or invalid fuel, capacity or consumption inputs must not be invented.
See [fuel selection rules](fuel-planning-rules.md) and
[onward planning](fuel-regions.md).

## Storage and paid calls

Route signatures and durable provider-request caching prevent unchanged work from
becoming another paid calculation. TomTom attempt reservations and daily accounting
remain server-owned. The configured shared limits are 1,000 uncached attempts per
UTC day and 30 per minute; failures count, cache hits do not. The separate
per-truck recalculation budget remains disabled in the current configuration.
These application limits are not provider billing or account-level quota guarantees.

Configuration binding and startup validation belong to API's composition root;
policies remain in Application. Controllers dispatch Application requests through
MediatR. Credentials remain server-side and must not appear in payloads or logs.

## Sources and historical evidence

The implementation owners are `RoutePlanningService`, `PlanningReadService`,
`AutomaticPlanningService`, `BaseRouteService`, and `DeadheadService`.
API contracts are defined by `Server/API/Controllers/RoutePlanningController.cs`
and `FleetController.cs`; do not copy an obsolete endpoint list from an audit.

The [earlier route-planning description](../archive/2026-09/route-planning-history.md)
is retained for context. Its automatic-fuel behavior and earlier limits are not
current operating instructions.
