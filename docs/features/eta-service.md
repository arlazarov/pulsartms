# ETA forecast

ETA is an estimated dispatch forecast, not an ELD compliance decision. Application
owns chain selection, input validation and the shared simulation. Infrastructure
provides local coordinate/timezone lookup, cached Samsara clocks, the refresh worker
and persistence through `IEtaForecastStore`.

## One forecast for the truck's ordered loads

The complete captured truck itinerary supplies the current and future loads
through the explicit ETA subset; Board rows only select trucks. An unfinished in-transit load or recorded pickup stays current even after
its scheduled delivery date; a calendar rollover is not completion. Preview and
live map reads use the same selection, so a missed appointment cannot silently
move the forecast root to the next load. A single HOS clock continues through the remaining current stops, each
saved delivery-to-pickup connection and each future load's saved road legs. The
future pickup does not start with fresh driver hours. Completed stops are omitted;
an arrival at the current facility uses recorded activity only when current GPS
agrees. `PickupMinutes` and `DeliveryMinutes` default to 120 minutes each, including
the final delivery before the next connection. Other intermediate stops use 60
minutes. Appointment waiting and service start/departure are distinct from arrival;
appointment slack can absorb an earlier delay. See [HOS planning](hos-eta-planning.md)
for duty and rest assumptions.

For current and future loads, appointment waiting and remaining facility service
are planned sleeper time, not cycle duty. The same clock combines consecutive
waiting/service and applies qualifying daily rest without adding the same rest
twice. The road forecast does not assume a cycle restart during a gap. This
approximate assumption does not record actual rest, mark a pickup complete or
change ELD data. Driving and once-per-planned-shift PTI/fueling allowances consume
Cycle; a midnight recap may increase it while the driver is at a stop.

The current planning response carries the whole chain, keyed by both dispatch and
stop ID. Dispatch cards/details receive only their load's stops. Missing or stale
future route inputs stop the forecast at that boundary without discarding valid
earlier stops. They never trigger routing or geocoding from the ETA calculation.

Missing-input and internal preparation reasons remain in the response for
diagnostics, not in visible cards or tooltips. An unfinished stop without an
eligible forecast displays one compact `ETA —`; unknown cycle/recap sections are
omitted. Pending empty mileage is `—`, never zero. Existing complete same-stop
forecasts retain their values and colors during refresh under the bounded grace
below. An unchanged usable route also hides known automatic queued/retry/budget
maintenance messages; actionable warnings and explicit fuel-action failures remain.

## Saved results and refresh

`DispatchEtaForecasts` stores one snapshot per load, with truck, current/root load,
driver, input signature, calculation/expiry times and the serialized per-stop
forecast. Migration `20260908160522_StoreDispatchEtaForecasts` is additive. Saved
base/deadhead route JSON already includes travel seconds; no duplicate travel-time
columns or provider calls are needed. Financial RPM remains a separate server-owned
calculation.

Before persistence, PlanningWorkPublication compares canonical work and
captured historical predecessor batches inside the same protected transaction
that writes the forecast batch. ETA calculation policy 16 includes historical
signatures in InputHash, including unavailable connections. Completed-history
changes can invalidate a forecast while leaving GeometryHash and compiled road
timing intact. The original historical lookup batches are retained through
validation. The memory entry is considered committed only after the
transaction commits. A validation or commit failure removes that calculation's
unpublished entry and preserves saved data. Provider/HOS work happens before
this transaction. Before writing the batch, ETA also validates the captured
routing dimensions against an uncached profile in the protected transaction.
It reuses the existing route-input signature; fuel-only preferences, exchange
rates and confirmation flags do not reject road ETA. Truck/fleet settings stay
protected through commit. Display descriptions retain their bounded profile
cache. ETA also compares captured saved-road metadata inside this transaction.
The current plan used by calculation must match the selected root metadata,
including plan/leg ownership, version and stop tracking. A newer database
description cannot certify an older cached current plan. Completed roots
skipped during selection remain dependencies; changing one rejects the old
selection. A missing road is also captured, so its arrival during calculation
requires a fresh forecast. Fuel-only root writes do not change road identity.
Routing owns the shared compact saved-plan metadata reader used by ETA and
fuel. Visited-stop dictionary keys are sorted before signature calculation;
JSON object key order does not invalidate an unchanged road. Policy 16 prevents
reuse of signatures generated before this normalization.

Cold future-timing compilation compares the actual loaded base/deadhead
metadata with the captured versions before filling the cache. Version metadata
includes geometry presence: clearing geometry while retaining financial miles
and its calculation date still invalidates the timing identity. Root and future
metadata are checked again under the publication lock, without transferring
geometry or calling providers. A conflict preserves saved forecasts and removes
the unpublished memory result; normal refresh can retry. Stale current-route
cache entries retain their existing bounded expiry/invalidation policy.

Telemetry retains its separate freshness rules; this boundary does not make
external observations transactional. The shared source lock and its PostgreSQL
performance limits are described in [route
planning](route-planning.md#captured-work-for-route-mutations).

Each stop also carries an optional `CycleAfterDeparture` snapshot from the same
simulation, after appointment waiting and service. Dispatch and map cards show a
separate `Cycle remaining` section under each unfinished pickup/delivery, including
the current load. Only the arrival balance is displayed inline with `Cycle remaining`;
the post-service balance remains calculation data, not a second visible row. Legacy snapshots
without the signed `Hours` contract retain their after-stop-only display.
The future-load summary shows the final stop's remaining cycle, so current work,
empty connections and all preceding loads are included. The Dispatch truck header's
single `Next recap` uses `CycleAtCalculation`: the nearest verified driver recap from the beginning
of the shared forecast, before assumed rest, travel or service. A future delivery
must not move this display past an earlier recap that the simulation already used.
The same baseline survives filtering the chain into individual load snapshots.
The Client only formats these server values; it does not replay HOS or make a
separate forecast request. The nested fields are
saved in the existing forecast JSON and require no schema migration. Older JSON
without these fields remains readable; chain policy version 8 invalidates old
snapshots for normal background refresh. Missing calculation baselines are not
replaced with a final-stop recap. A recap timestamp already reached is not displayed
as the next future event while a newer snapshot is pending.

The board exposes this baseline once as optional `CurrentCycle`, containing the
original calculation/validity times and `Cycle`. It reuses the already batch-read
root forecast only when truck, current/root load, driver and input signature match.
A pending forecast retains the same snapshot through the existing 15-minute
display-only grace after its original validity deadline; reads do not extend that
deadline or claim a new calculation. Beyond that grace, or once the recap is
already reached, unverified or missing, the value stays unknown,
including trucks without a current forecast. Internal `IncludeEta=false` reads do
not enrich it. This adds no history/provider request or per-load recap calculation;
the Client formats the recorded driver-home date and minutes, never a future-stop
recap or a browser-timezone substitute.

`DutyStatus` also carries optional `CycleResetHours`, `CycleResetCountry` and
server-computed `CycleResetRemainingMinutes`. These describe the current verified
continuous rest under the fresh truck-position region, not future-stop rest or
an assumed restart. Missing regional/rule/history inputs leave the countdown
unavailable. The additive fields remain in forecast JSON; no migration is needed.

Legacy cycle snapshot minutes are nonnegative and rounded down. The new signed
planning balances are described below. Next recap is the first future
driver home-day boundary with positive usable hours returning, not automatically
the next midnight and not a cycle restart. Its timestamp retains the driver's
home offset and timezone, including configured day start and DST. Verified rolling
duty totals are required; missing, inconsistent or cross-border history leaves
recap unknown. The bounded search covers at most one configured cycle and excludes
pre-restart duty. Canada cycle 2 retains its current secondary duty ceiling rather
than assuming a future 24-hour rest for display. No future work beyond that stop
is invented when finding the next recap.

These snapshots are observational: they do not advance the clock, alter ETA or grant
hours to later loads. Card values are explicitly approximate, scoped to the exact
stop/dispatch and share the existing pending-update display grace. Missing, invalid
or expired stop snapshots show a dash instead of borrowing another stop's cycle.
Completed stops omit this future estimate. Formatting is shared between per-stop
values and the final-stop summary; no extra requests or timers are added.

Reads batch-load snapshots and validate the ordered chain, assignments, route
versions, schedules, actual stop activity, driver and planning policy. An inserted
load, reassignment or edited appointment invalidates the affected chain. Snapshots
cannot be borrowed from another truck or predecessor. Conditional, transactional
upserts reject an older concurrent batch rather than partially replacing a newer
forecast. Persisted estimates are display snapshots, not authoritative dispatch
status or actual arrival records.

Results expire after 120 seconds (`EtaMemory.RefreshInterval`), and the same
interval is the longest the worker sleeps. It wakes at the earliest expiry of a
forecast still to expire, so a forecast is due when it expires rather than a
whole interval later. Events wake it at once: new view demand, a changed input
signature, a read that finds the forecast's road moved on (a new route version,
a passed stop, a confirmed deviation) or its work changed, and a driver duty
change. A duty change is a different duty status between two readings or a
driver's first reading; clocks counting down under the same status are not one.
A forecast made without hours holds for the usual interval, and that driver's
first reading makes it due. Missing, superseded and failed forecasts are
retried at the interval, not in a loop. Wake signals coalesce. Up to two
refreshes run in independent scopes, each with a thirty-second timeout.
Cancelled or superseded
calculations cannot publish a late result. Instances have separate memory caches
and share the saved snapshots. Cards consume accepted planning responses without
extra HTTP requests or changing existing polling intervals. This interval sets
how often a forecast is recalculated; its CPU and database cost against the
previous ten-second sweep has not been measured.

Running work is kept viewed by the background planning summary pass (see
[fleet efficiency](../architecture/fleet-efficiency.md)), so its forecasts are
calculated with no page open. The ten-minute idle eviction applies only to work
that is neither running nor being looked at.

A forecast that expired, or whose road moved on, is kept for the same work - the
same truck, load, leg, assignment and stops - and is returned marked
`RouteUpdatePending` until its replacement lands; the map reads it the way the
Dispatch board already reads saved forecasts. A forecast for other work is never
returned in its place.

The panel/card retains the whole matching complete snapshot from the start of its
existing refresh request, including ETA, cycle, recap and duty status. A newer partial pending
response cannot erase it. Retention is limited to fifteen minutes after original
expiry. Its saved text, values and status colors remain unchanged, without
`Previous` or `Updating` labels. Retained status is the previous result, not a
new server calculation. Dispatch's route summary also keeps
its complete plan during an explicitly pending response without a plan, under the
same truck/load identity and bounded grace. Fleet Map can retain missing-plan
geometry only when the response wrapper matches the saved truck/dispatch and
does not change or complete the current stop. This is a display-only projection;
the cached provider response remains unchanged. Current and future stop cards
retain their matching ETA without inserting a contradictory missing-value row.
The current map popup uses the same quiet retention behavior.
Map request-start metadata extends only the display deadline, without publishing
route geometry or adding HTTP/provider calls. A geometry-only plan/version change
keeps the matching stop forecast and visible distance while using the new geometry's
own progress coordinates. Future loads inspect their own `PendingDispatches` entry
even when the current stop's forecast is complete. Transient same-query Dispatch
failures keep mounted cards; changed queries and denied access do not retain the
previous board as if it belonged to the new request.
Changing the truck, dispatch, stop, known destination or appointment,
or completing the stop, clears display memory. An explicitly non-pending unavailable
replacement is not treated as an in-flight calculation. Out-of-order results cannot replace
newer values. Persisting or retaining an estimate does not extend its validity.

The compact English-only stop view uses `Road ETA`, `Cycle at arrival`,
`Cycle after stop`, `Next Recap`, `With Recap` and `If reset`. It does not repeat
the cumulative driving/rest explanatory block. Those existing duration fields
remain in the response for diagnostics.

## Cycle feasibility and conditional alternatives

`HosCycleFeasibility` is a pure Application algorithm over verified duty history
and chronological work/rest events. `HosTravelClock` continues to own daily driving,
shift, break and service timing. The road forecast observes cycle usage without
inserting a cycle wait or restart. No routing, geocoding, database or provider reads
occur inside either replay.

Cards label this arrival time simply `ETA`; the shorter label does not change
the road-only calculation or its separate cycle-feasibility warnings.

Each stop's additive `Hours` object contains signed `CycleAtArrivalMinutes` and
an optional `CurrentCycleMinutes` for an already observed, still-active facility
visit. A live clock cannot reconstruct an earlier arrival balance. The arrival
field stays null for that case; UI shows the separately supplied current balance
under Cycle remaining, with a current-stop tooltip. It never borrows the later
departure balance or another stop's clocks. The object also contains
`CycleAfterStopMinutes`, `CycleVerified`, `FirstCycleShortageAt` and
`DrivingShortfallMinutes`. The shortfall is peak projected driving debt in the
preceding chain, not elapsed lateness and not the minimum additional capacity
needed to meet an appointment. A later recap cannot erase an earlier period of
driving without sufficient cycle. Work is split at actual home-day boundaries;
tomorrow's hours cannot be borrowed today. Planned sleeper service does not spend
Cycle; the after-service balance can increase when a verified recap occurs during
the stop. A negative balance carried into the stop is not silently cleared.

Current Samsara Cycle is the authoritative starting balance, not a value
reconstructed from historical totals. With a continuous, fresh observed suffix
and a supported rule, missing older days or a totals mismatch do not discard
that balance: forecast driving
and on-duty work subtract from it, while rest does not. History must reconcile
within 15 minutes to enable recap. Larger mismatches keep signed balances available
but do not release historical credits or advertise a recap date. This is a
conservative projection, not verified future availability from unreconciled logs.
An explicit completed restart establishes a new full-cycle anchor; baseline waits
never silently do so. Cycle projection can trim only a missing leading interval;
it never fills that interval with invented duty/rest. Historical recap requires
coverage of the applicable cycle window and reconciliation with ELD. The full
timeline used for daily/split-rest credits is unchanged. Missing/stale recent
history, internal gaps, conflicting records, unknown rules or jurisdiction changes
still leave signed feasibility unknown. Canada's additional Cycle 2 history
constraint remains enforced. No extra label is added to the normal balance row.
Daily-HOS road timing remains available when its own
inputs are valid. Independently known road lateness remains visible even when
cycle feasibility is unknown. Old payloads without `Hours` retain the legacy ETA
presentation rather than being mislabeled as road-only forecasts.

When a verified baseline encounters a driving shortage, the prepared chain can be
replayed for two separate alternatives: waiting for usable recap and taking a
qualifying restart. Replays share saved timing projections and are bounded; they
do not run once per stop or search minute-by-minute for virtual cycle credits.
All modes stop at a ninety-day horizon, retaining completed forecast stops and
marking the remaining chain pending rather than discarding the earlier results.
Zero-return home days are skipped when finding usable recap. A recap wait that
already qualifies as daily rest refreshes daily budgets without another full
sleep. A restart includes an eligible ongoing rest and never adds a separate
ten-hour sleep on top. Future assumed downtime is distinguished from observed
history so the baseline cannot silently gain a restart.

Alternatives preserve dispatch/stop identity, prior services and appointment
waiting. Recap alternatives can be late; restart alternatives are displayed only
when they meet a known appointment. `On time if reset` remains conditional, never
a statement of driver intent. A road-only timely arrival with insufficient cycle
shows `Cycle short`, not an unqualified green `On time`. Retained pending values
keep their saved presentation within the same bounded display grace.
The existing forecast JSON persists these additive fields; no schema migration
or extra paid requests are required.

## Cheap local travel replay

Travel uses remaining distances and saved truck-route leg durations. Country
segments are compiled once per immutable plan/version, preserving geometry vertices
and regional sampling at most two miles apart. Adjacent samples in the same country
and latitude ruleset merge. A bounded per-instance LRU holds at most 128 profiles
and 32,768 leg/segment units, with fixed compilation gates and no retained geometry.
Warm calculations replay those segments at progress, fuel, stop and HOS boundaries
instead of walking all coordinates again. Current-position and destination timezone
lookups remain live local lookups. A new geometry version recompiles its profile;
HOS and GPS changes reuse the profile without extending ETA validity. This does not
call a routing API every two minutes. Arrival timestamps use the destination IANA
timezone (GeoTimeZone); appointment windows use their end. Missing appointment
times and ambiguous/nonexistent DST appointment times do not produce a lateness verdict.

Standard US property rules: 11h drive, 14h window, 30m interruption after 8h driving,
10h daily rest. A verified US restart is a separate 34-hour scenario, not part of
the road-only ETA. Unknown cycle history remains unknown. Standard Canada south: 13h drive,
14h duty; the fallback spaces work/rest patterns over at least 24h with at least
10h rest. Verified Canadian alternatives use their configured restart duration.
The road forecast never grants extra hours at a border.
Current remaining limits come from Samsara. History-based credits are described
below. Exceptions, border delays and exact facility service duration are not
modeled. `EtaPlanningOptions` supplies one 5-minute on-duty fuel allowance per new
planned shift, alongside 15 minutes PTI. Saved fuel recommendations do not add
repeated allowances or independently invalidate ETA. Facility service defaults
to sleeper time; a short stop alone is not a daily reset. Every result is
explicitly Estimated and includes its assumptions.

Unsupported regions, stale HOS/GPS and changed/off-route geometry produce an unavailable result instead of a false On time verdict. Canadian north currently requires verification and is unavailable. This conservative model can overestimate rest and lateness; it is not a substitute for the driver's ELD.

Sources checked 2026-09-05:
- https://www.fmcsa.dot.gov/regulations/hours-service/summary-hours-service-regulations
- https://laws.justice.gc.ca/eng/regulations/SOR-2005-313/FullText.html
- https://developers.samsara.com/reference/gethosclocks
- https://github.com/mattjohnsonpint/GeoTimeZone

## HOS history update

Samsara HOS logs are paginated and grouped by driver, with a 16-day initial window in memory. The last two days are replaced on each one-minute cache refresh; a full 16-day refresh every 15 minutes picks up older edits. Driver timezone/day-start/cycle settings are refreshed every 15 minutes. All periods must be continuous and recognized; gaps/conflicting overlaps disable history credit. Cycle totals must agree with current Samsara clocks within 15 minutes before recap is allowed. Daily duty hours expire at the driver's home-day boundary (DST aware), including forecast duty. A detected cycle restart excludes earlier work. Canada cycle 2 also limits duty since a 24-hour rest to 70 hours.

For reconciled history, historical credit is capped by ELD-used hours at the anchor.
Excess historical duty withholds the earliest credits rather than returning hours
twice. Additional ELD-used time not found in history stays charged through the
anchor home-day's cycle window. That discrepancy expires with the historical
window; it is not a permanent offset or extra cycle capacity. Post-anchor duty is
counted separately and cannot be credited away with an older historical mismatch.

The split planner considers a verified earlier qualifying period and a complementary future sleeper period (US 7+/2+ with total 10; Canada two sleeper periods >=2h totaling10), checks preceding work, and deducts work between the periods instead of awarding a fresh shift. It can credit an already ongoing qualifying rest. Missing anchors, incomplete logs, unknown rules and cross-border changes use the conservative fallback. Rolling split chains without a verifiable full-rest anchor are not assumed. Canadian daily forecast remains conservative. This remains a forecast, not an ELD certification.
