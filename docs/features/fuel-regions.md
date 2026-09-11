# Fuel planning beyond delivery

## Manual rolling horizon

Fuel search runs only through Calculate Fuel / Recalculate Fuel. Background
tracking prepares routes but does not replace purchase recommendations.
The global Settings preference determines IFTA eligibility and economic prices,
independently of the map's display toggle.

The search starts at fresh GPS and keeps every remaining mandatory current stop.
It appends all subsequent assigned trips, including overdue unfinished trips and
connecting deadhead. This is an opt-in fuel scope, not a change to ordinary
Dispatch board filtering. There
is no mileage, 72-hour date, or two-trip cutoff. The complete itinerary must fit
the 40-mandatory-stop routing envelope; otherwise calculation fails and retains
the saved plan instead of presenting partial coverage as complete. Candidate and
road-check budgets remain bounded independently of the number of assigned loads.

Saved matching base routes and deadhead connections are reused where applicable.
Changes to assignment, profile or stops invalidate reuse; ordinary movement does
not change a fixed delivery-to-pickup connection. The final assignment signature
is checked again before storing recommendations.

## Route and purchase safety

Variants keep identical mandatory endpoints and ordered stop boundaries.
Fuel recommendations are stored separately: future-trip geometry must not replace
the current dispatch's route, distance or version.

Starting fuel uses the latest valid available percentage with an observation
timestamp, even when old. Missing timestamps and observations more than one minute
in the future prevent automatic fuel calculation before the search starts; an
explicit manual quantity does not require a sensor timestamp. This is an estimate,
not a claim of live tank accuracy. Missing or invalid percentages prevent automatic
calculation. Fresh GPS, valid capacity/consumption, reachability and reserve
constraints still apply.
The immediate recalculation response uses the same validation as ordinary planning
reads: a valid last-reported fuel level with a nonfuture timestamp or an explicit
recent manual quantity is needed to validate them. Reading age alone does not
invalidate quantities or trigger assumed consumption. Calculation permission is
not a claim of live tank accuracy.

The terminal policy preserves reserve plus checked fuel access after delivery.
An expensive or poorly served destination requires the configured additional
arrival buffer. Comparisons use the same terminal policy; an infeasible checked
plan is not reported as feasible by dropping the reserve.

Checked detours are compared economically rather than rejected at a fixed number
of minutes or miles. Purchase quantities include the extra road consumption;
ranking includes no fixed purchase-stop fee, but still charges extra driving time
and additional schedule delay without double counting. Existing appointment and
cycle preferences remain ahead of cost. Geographic and provider-check budgets
remain bounded, so this does not start an unlimited search.

Starting recalculation leaves the previous display in place. Successful validation
replaces the route compatibility copy and truck-owned snapshot atomically. Failure
does not erase the durable plan; the client then performs one normal read to check
whether its saved quantities remain actionable. It does not start another search.
Settings, prices, ordered assignments, route matching and an invalid or missing
tank observation can invalidate a saved recommendation. Internal invalidation
reasons do not become maintenance messages in the UI. Ordinary station markers
remain controlled by Fuel Stations.

The truck snapshot survives completion of its original dispatch. Subsequent loads
reuse their remaining ordered stops and repeat visits to the same station retain
separate identities. Fresh readings update the projected tank balance; passing a
station never confirms that a planned purchase actually occurred. A manual
quantity is retained over older telemetry for at most 30 minutes, unless a new
reading arrives or a planned purchase has been passed without confirmation.

Each saved purchase carries its order number across current and future loads.
A shared physical marker shows the associated visit numbers, while its popup
keeps separate compact cards with purchase amounts, truck-relative distances,
and circular on-arrival/after-fueling indicators for each visit. Gauge percentages
use configured tank capacity; the Client does not recalculate purchase economics.

The poor-region arrival minimum remains at least half the working tank, or the
checked escape fuel plus reserve when greater. The economic target extends to the
configured fill limit so useful residual fuel is valued consistently; this target
does not force a fill or relax the minimum.

## Bounded search and limitations

`FuelRegionOptions` owns candidate, geographic and road-check limits. Local
comparison shortlists candidates before requesting complete truck routes.
Post-delivery access checks have a separate bounded budget; provider cache and
shared attempt limits still apply. This is a search among checked alternatives,
not proof of a global optimum.

The shared road-ETA module compares appointments and cycle effects using one
cached driver-clock/history snapshot per manual search. It does not query HOS per
station or variant, and the preview does not populate the live timing cache.
The normal planning read keeps the existing HOS provider cache policy; its cache
expiry can still trigger the usual telemetry request, independently of fuel search.
Bounded regional sampling keeps dense route geometry from multiplying region
lookups by its vertex count. Unknown clocks, history, appointments or exceeded
preview bounds remain explicit; the UI shows the dated check, not a continuously
verified HOS plan. Parking, yard/pump availability, toll differences and actual
fuel-stop service durations still require review. Recommendations are not
automatically sent to drivers.

See [purchase rules](fuel-planning-rules.md), [route planning](route-planning.md),
and the implementation owners `FuelHorizon`, `FuelPlanningService`,
`FuelRegionPlanner`, `TruckFuelPlans`, `FuelPlanProjection`, `FuelScheduleEvaluator`
and `FuelRegionOptions`.

`StoreTruckFuelPlans` is an additive schema migration. Apply it before deploying
code that reads the truck snapshot table. No backfill is required: legacy route
recommendations remain readable until the first successful explicit calculation
creates a truck-owned snapshot. Do not use application/production data as a
migration test fixture.

The [earlier design record](../archive/2026-09/fuel-regions-history.md) retains
superseded automatic/full-tank descriptions and development notes.
