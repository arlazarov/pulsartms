# Truck route planning

## Display and ownership

Fleet Map and Dispatch display saved planning results. Application resolves the
current dispatch from authoritative truck assignments and stop progress; the
Client must not guess that identity from an ordered list of future loads.
A missing route does not make the current load a future load.

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

Opening or polling the map does not launch a fuel-purchase search. The dispatcher
uses Calculate Fuel / Recalculate Fuel. Background tracking may invalidate saved
recommendations but does not replace them with a new purchase plan.

Recalculation keeps the previous display until a complete replacement is saved.
The truck-owned plan and current-route compatibility copy commit atomically;
failure preserves the prior snapshot but a normal read must independently validate
its assignments, prices, settings, road position and fuel level before presenting
actionable purchases. Current route geometry remains separate from the wider
fuel-planning horizon, which can continue into the next assigned load.

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
