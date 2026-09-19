# Operational mileage attribution

This module separates physical movement, distance evidence and load attribution.
It does not calculate driver pay, customer charges or revenue allocation.
The operational model is described in
[dispatch execution](../architecture/dispatch-execution-and-settlements.md).

## Dispatcher and accounting views

Load Details retains its existing route-based Empty, Loaded and Total figures.
The optional Mileage breakdown reads allocation information on demand. It does
not call routing, geocoding, GPS or HOS providers, create movements, or write
evidence. Opening and closing the panel is not a dispatch operation.

The breakdown separates planned estimates from recorded actual distance. Empty
includes bobtail; showing its bobtail subset must not add those miles twice.
Missing evidence is not zero. A bounded or incomplete breakdown must not imply
that its subtotal covers the complete load or trip.

For loads without native execution or movement records, the read uses the
existing validated planned deadhead distance and imported loaded-distance
estimate. Those are labeled planned, never measured. Existing financial route
totals are not overwritten by policy changes or this compatibility projection.

## Allocation rules

- Loaded movement follows its explicitly carried load.
- Pickup approach follows the load being collected.
- Home, yard return, maintenance and repositioning use the saved company rule:
  previous load, next load or unallocated.
- Missing target context stays unallocated with a reason. Do not infer a load
  solely from nearby coordinates, a truck's current catalog assignment or an
  incomplete provider payload.

Admin edits company rules in Settings. Rules default to unallocated for purposes
without a universally safe load target. They are not individual display units.
Each movement records the rule revision and resolved attribution. Later rule
edits do not rewrite existing movement history.

An authorized allocation correction requires the opened movement revision and a
reason. It appends an event identifying the previous and new allocation, actor
and time. Returning to automatic allocation explicitly evaluates the current
policy; it is not an unnoticed consequence of refreshing the page.

For delivery A -> home -> pickup B, record separate movements and keep the
intervening stationary period separate from truck mileage. The home rule applies
to the first movement; the confirmed
pickup approach belongs to B. Travel in a personal car is not truck mileage.
Purpose never determines HOS or Personal Conveyance classification.

## Evidence and concurrency

Movement identity owns truck, driver/crew, trailer, purpose, cargo state,
endpoints and optional execution-leg context. It can exist without a load.
Truck mileage is not duplicated for a co-driver or an additional cargo link.
Distances use decimal miles, consistent with existing server route contracts;
Client distance controls perform the existing display-unit conversion only.

Distance evidence is append-only. Planned and actual evidence have independent
current references. Actual distance requires a recorded physical interval and
an explicit source; a planned route is not an automatic actual-mileage fallback.
Manual entry is labeled manual and cannot impersonate provider telemetry.

Actual observations, native operational events and provider ingestion are
separate responsibilities. Neither missing odometer samples nor GPS traces
become inferred actual distance. A routing recalculation is not driving evidence.

## Automatic capture

The durable execution-planning queue records the saved full native road route
after its assignment revision, input signature and stop anchoring match. It
creates one planned movement per adjacent stop segment. It does not use a
GPS-start remainder as the full segment. Stable segment identities make retries
idempotent; a changed road appends evidence and removed segments are superseded,
not deleted. Existing manually overridden allocations are not silently reset.
The original legacy imported and deadhead figures remain separately readable in
the existing route totals; native records do not manufacture replacement actuals.

The independent Samsara odometer job polls `obdOdometerMeters` on its own feed,
lease and cursor. It does not change the existing GPS/engine/fuel feed parameters
or cursor. OBD values are cumulative measured meters; actual miles use the
difference between two accepted values. GPS-derived odometers and straight-line
distances are not fallback evidence. Samsara documents OBD coverage as distinct
from GPS-derived distance. See the
[vehicle stats feed](https://developers.samsara.com/reference/getvehiclestatsfeed)
and [distance guide](https://developers.samsara.com/docs/calculating-distance-traveled-guide).

The first sample only establishes an anchor. A pair cannot be attributed before
the assignment was recorded or started, or across an assignment completion.
Both cargo-segment boundaries must have factual stop events. Future appointments
do not establish those boundaries. Unconfirmed handoffs, source review, ambiguous
legs or unknown cargo prevent attribution; raw pairs can wait for later factual
confirmation. Exact sample endpoints are retained without interpolation back to
a pickup or forward to a delivery. Current catalog driver changes do not replace
the native leg's driver, co-driver or trailer.

Intervals with conflicting values, resets, gaps over six hours or implausible
increases become explicit capture gaps. Unresolved pairs have a seven-day
staging retention target; bounded cleanup converts expired pending pairs to gaps
and removes processed staging rows. Movement evidence retains its measured raw
endpoint values and timestamps, and gaps remain retained. Read summaries show
pending counts and bounded recent gaps. Actual totals are observed subtotals,
not a claim that the complete trip has been captured.

The job consumes one bounded page at a time. Cursor, positions, staging and
resulting evidence commit together. Serializable transactions, revision checks,
stable identities and same-truck overlap guards prevent replayed pages or manual
records from counting physical work twice. Contiguous sample pairs within the
same confirmed segment can share one movement/evidence interval.

Automatic route rows contribute only to planned totals; automatic OBD rows only
to actual totals. Their absent opposite basis is not missing evidence. Automatic
distances cannot be overwritten through the manual-distance endpoint; audited
allocation corrections remain available. System-origin evidence uses the empty
actor GUID together with its explicit automatic origin, not a dispatcher identity.
No driver-pay, revenue-sharing or ELD classification formula is introduced.

Commands enforce active-account roles, optimistic revisions and transactional
updates. Movement creation is idempotent. Physical intervals are checked for
same-truck overlap; adjacent intervals remain valid. Concurrent edits cannot
silently replace another user's attribution or evidence.

## API and rollout

- `GET /api/dispatch/{id}/mileage-breakdown`
- `GET /api/settings/mileage-policy`
- `PUT /api/settings/mileage-policy` (Admin)
- `GET /api/mileage/unallocated`
- `POST /api/mileage/movements`
- `PUT /api/mileage/movements/{id}/allocation`
- `PUT /api/mileage/movements/{id}/distance`

The execution and mileage schema additions must precede starting an API that
reads these tables. Scaffolding uses the design-time context with an inert local
connection string; it does not load application credentials or start workers.
Scaffolding is not applying migrations. Deployment and database application
require the normal explicit authorization and release process.

Migrations: `20260914022232_AddExecutionAndMileage` and
`20260914031300_FinishSwitchAndAutomaticMileage`, applied on 2026-09-14.
See the [release evidence][execution-release] for backup and verification limits.
They also add resource-configuration provenance and native planning keys.
This is not a rolling-compatible schema change: drain and stop old API instances
and background writers before applying it, then start the matching application.
Old ETA upserts require an unfiltered unique index that this migration replaces.
Do not enable native transfers while any old reader or writer remains active.

Rollback refuses to discard recorded trips, transfers, movements, allocation
policies or local resource configuration. Once those exist, use a reviewed
forward repair; restoring a backup is a separate, explicitly approved operation.
Append-only audit behavior is enforced by Application commands and EF mappings,
not by tamper-proof database triggers.

Test selection: shared persistence/contracts require `bash test.sh all`, with
Client build and style compilation for the new views. Passing a build or an
in-memory fixture does not establish PostgreSQL behavior or visual correctness.

[execution-release]: ../archive/2026-09/execution-release-2026-09-14.md
