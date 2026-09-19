# Route and fuel selection rules

## Calculation outcomes and validity

Fuel builds and resets return `FuelCalculationResult`. `Feasible` and
`FeasibleBelowReserve` include a complete plan; `UnreachableStation` and
`NoFeasiblePlan` include diagnostic access information without purchase or
financial totals. Automatic recalculation exposes the same `FuelStatus`.
Expected infeasibility is not an exception after a successful write. Invalid
inputs, changed dependencies and technical failures still reject publication.

Physical arrival fuel at the first purchase may be zero. Preferred reserve,
post-purchase reserve and the regional terminal arrival requirement remain
separate constraints. A reachable first purchase below preferred reserve is a
valid plan with a warning; a negative physical arrival is a diagnostic candidate.
The regional half-tank terminal policy is unchanged.

Diagnostic quantities use the requested effective profile. Publication validates
captured work, every selected road and predecessor-history dependency, observed
settings and the current fuel/GPS values. Reads invalidate diagnostics when those
inputs change. Observation times remain provenance; a repeated observation with
identical quantities and position does not invalidate the calculation. A short
age limit also bounds price-based diagnostics, but does not replace input checks.
The map formats numeric shortfall values and never treats a diagnostic candidate
as an executable fuel purchase. A valid diagnostic suppresses older feasible-plan
financials while preserving the saved truck snapshot for subsequent recovery.

## Inputs

Use fresh GPS, every remaining assigned load in dispatch order (including overdue
unfinished loads), the latest valid reported fuel level, dated road-diesel prices,
and effective truck settings. Do not truncate at 1,800 miles, 72 hours or two future
loads. Ordinary Dispatch date filtering is unchanged.

Reading age alone does not invalidate a reported fuel level. Do not invent burn
after the reading or credit a planned purchase as an actual purchase. Missing or
invalid levels remain unavailable. Observation timestamps permit one minute of
future clock skew. Explicit manual gallons take precedence over older observations
for at most 30 minutes and expire after an unconfirmed passed purchase.

Application owns financial formulas. Client formats server quantities only.
An explicit fleet CAD-to-USD preference takes precedence. Otherwise effective
profiles read the dated, stored Bank of Canada daily-average rate. The existing
fleet synchronization cycle checks the fixed public FX feed hourly under a
separate checkpoint lease; profile, route and map reads never call that provider.
Invert the published USD-to-CAD observation, retaining its observation and
retrieval dates. Reject invalid, future or more-than-seven-day-old observations.
Provider failures retain the last usable rate without inventing a conversion or
overwriting fleet preferences. See the official
[Bank of Canada Valet API](https://www.bankofcanada.ca/valet/docs).
Changed effective fuel preferences, including an updated exchange rate, refresh
automatic fuel snapshots through the existing revision guard. They do not rebuild
unchanged roads or invalidate an unchanged road-choice preview. Manual fuel plans
remain protected.
Automatic searches price each station occurrence for its estimated local arrival
date, replaying the captured HOS and appointment waits. Candidate screening uses
road-only arrival estimates; shortlisted chains are repriced with their estimated
access driving before quantity optimization. Missing arrival-date quotes fall back
to today's eligible quote and are marked estimated. Unknown ETA also uses an
estimated current quote, never an invented arrival date. Reads are shared per date
within a search and use the existing server price cache, without provider calls.

The lease-owned fleet planning cycle refreshes existing automatic fuel
snapshots when the USD diesel quote fingerprint changes for today or any
pricing date used by the saved search. This includes newly published tomorrow
prices before they become effective today. Identical quotes and Canadian-only
changes do not trigger that refresh. The fingerprint is saved with the
successful plan, so failed work retries and application restarts do not lose
pending refreshes. Legacy snapshots without a fingerprint are refreshed once
when eligible. Manual edits and manual starting fuel are preserved; the opened
revision is rechecked inside the truck calculation gate. Missing GPS, invalid
routes or failed calculations never erase the saved plan. Cadence follows the
existing bounded per-truck planning scan, not a guarantee that every truck
recalculates immediately after an email arrives. An older automatic
selection-policy version also triggers a refresh, even when the quote calendar
is unchanged. Manual snapshots are never reset by that upgrade. The refresh
also compares saved assignment signatures against the authoritative truck
itinerary. Added, removed or edited future loads therefore request a new
automatic plan without waiting for new prices. Eligibility uses the bounded
captured-work display cache without HOS, financial or ETA hydration. The
calculation itself captures fresh work and does not trust that cache to
certify publication. PlanningWorkPublication repeats the complete signature
comparison inside the protected transaction that replaces both saved fuel
copies. It also replays captured historical predecessor batches for horizon
connections and the onward arrival policy, after validating their current
work. Completed-history changes reject automatic and manual saves before
profile or result writes. This protects captured work membership and facts
through commit; prices and telemetry keep their separate policies.
No route provider is called for this check. See [route
planning](route-planning.md#captured-work-for-route-mutations) for the current
publication lock scope and unmeasured PostgreSQL contention.

Calculation captures immutable versions of the actual saved roads it consumes:
the current plan, future base roads or full-plan fallbacks, connecting deadhead
and any onward connection used by arrival policy. An absent base remains a
dependency when it selects the fallback. Onward roads remain dependencies even
when no eligible exit price is found. Every repeated observation is checked;
deduplicating lookup keys must not discard an earlier version.

SavedRoadValidation compares these versions inside the same publication
transaction, before profile or fuel writes. The root version comes from the row
that supplied its geometry, including a cached row. A changed owner, plan
version, tracking state, connection or base-road version rejects automatic
calculation, manual edits and reset to automatic. Final validation reads compact
metadata without geometry or provider calls. Fuel-only annotations and visited
dictionary key order do not change road identity. Existing optimistic result
checks remain required; this guard does not permit overwriting a newer result.

New truck snapshots retain a versioned road-dependency list in their existing
summary JSON. It contains the consumed root, future base/fallback and connecting
roads, including absent base selection and any onward connection. No geometry
is duplicated. Ordinary progress is a separate publication observation and is
not persisted as a road dependency. A changed plan version, owner, assignment,
input hash, base/connection timestamp or geometry presence invalidates reuse;
tracking-only and fuel-only writes do not.

TruckFuelPlans validates dependencies for the remaining dispatch blocks and
onward connection through uncached compact metadata in one execution read
snapshot. Completed earlier blocks no longer participate. A mismatch, missing
legacy evidence or unsupported dependency version marks the retained projection
NeedsRefresh, removes schedule impact and purchase-dependent stop arrivals, and
leaves the stored plan unchanged. Existing consumers withhold stale
recommendations. This read calls no route/geocoding provider and does not repair
roads. The saved-summary and geometry caches retain their existing bounds.

The existing automatic refresh cycle uses the same road validation before
checking quotes. It requests recalculation even when prices are unchanged;
manual purchases and manual starting fuel remain excluded. A failed refresh
keeps the old plan for retry under the existing opened-revision check. Legacy
automatic snapshots receive dependency evidence on their next successful
calculation. No immediate refresh, production throughput gain or continuously
valid snapshot across separate HTTP requests is promised.

Historical corrections that do not yet change saved road metadata still need
persisted historical selection evidence and a separate refresh rule. The
publication history guard remains active; road dependencies alone do not close
that post-commit gap.

Calculation does not save the requested truck profile before price reads or
optimization. Successful publication commits that profile with both fuel copies;
provider failure, changed work, result conflict or failed commit preserves all
three previous records. Manual edits use the same publication owner. Low-level
profile and route-copy fuel writers require an existing transaction and are not
public service entry points.

Before writing, the effective profile observed at calculation start is compared
with uncached stored profile, fleet settings and exchange-rate reads. Fleet
defaults, explicit exchange-rate precedence and saved-rate validity rules apply
identically to display and uncached reads. Uncached validation neither requests a
provider nor publishes values into the display cache. A changed effective value
rejects the calculation; it cannot be hidden by a warm cache or overwritten by
the requested profile. The publication scope protects TruckPlanningProfiles
and FleetPlanningSettings through commit, including initially absent rows.
Automatic exchange-rate saves join the same protected publication scope, so a
stored observation cannot change between final validation and result commit.
This includes the first usable observation after a missing rate. The exchange checkpoint writer participates in the global planning guard;
other SynchronizationCheckpoints rows remain independent. Lease acquisition/release and the bank
request stay outside the protected write. Cache invalidation follows commit;
a failed write keeps the previous saved and cached observation. Explicit fleet
rates retain precedence, and refreshing an unused automatic observation does
not invalidate that effective profile. Clock-based rate age checks remain the
existing read-time policy; this does not freeze time or add a durable financial
rate record.

Application uses a 250-US-gallon total tank capacity and fleet defaults of a 100% fill limit, a 25-US-gallon reserve
and $35 per driving hour, with zero fixed purchase-stop charge. Legacy saved overrides for these fields are
ignored when reading effective settings; reading does not rewrite stored settings.
Explicit settings saves validate the request before applying these defaults and
preserve the IFTA basis, legacy detour setting and exchange rate. A saved $20
stop charge is ignored by effective settings without rewriting the stored row.
Consumption and the remaining preferences come from the effective profile.

## Saved-route planning without fuel routing requests

An automatic calculation reuses its converted station quotes per pricing date
across candidate chains. IFTA or exchange-rate changes invalidate that local
conversion cache. Candidate purchases receive independent mutable stop objects;
the cache lives only for that calculation and does not survive price imports.

Manual fuel calculation does not call routing/geocoding providers, rebuild roads,
repair connections or advance route tracking. Read the current saved road and saved
base/deadhead geometry for all remaining assignments. Ordinary route preparation
and tracking remain separate responsibilities.

FuelWorkInputs selects work from the complete immutable truck itinerary. Search
and edit capture fresh facts once; horizon assembly and terminal fuel access use
that same capture. They do not query screen rows or resolve assignments again.
The current per-dispatch lookup locates the requested truck/leg, after which
captured facts supply the route input. Unresolved selected work fails without
replacing the saved result. Legacy planned work remains outside fuel scope;
overdue assigned work remains included.

An active execution received through a confirmed Hook may continue into the
truck's uniquely assigned future loads when it ends at an ordinary Delivery.
Preserve the execution leg and revision on each itinerary stop; future legacy
loads do not inherit the current leg. Drop, Switch and unconfirmed or different
native legs remain boundaries. A planned duplicate of the current commercial
load is not a second fuel itinerary. Saved-plan validation checks every included
assignment and stop before reuse, including progression to a saved future load.
The connection after native delivery uses that execution's truck and endpoint,
not the older trucks retained in its source load.

Saved paths must match profiles, confirmed facilities, assignments and remaining
stop identities. Facility snapping remains within 0.5 mile and adjacent endpoints
within 0.05 mile. Missing, stale or disconnected geometry fails without replacing
the previous fuel result. Never substitute a straight line for a mandatory road.
More than 40 mandatory stops or 200,000 saved leg points fails without truncation.

Consider station occurrences within forty geographic miles of the saved road.
These are nearby candidates, not verified truck approaches. Preserve mandatory-leg
identity, including outbound and return visits to the same station. Original road
geometry and provider mileage determine along-route distance.
Estimated station access stays in the country of its matched saved road point.
Resolve both points through the existing local region lookup; unknown geography
or conflicting station country data cannot certify access. Cross-border assigned
routes retain stations on each country's own road sections. Post-delivery access
stays in the destination country unless a matching saved onward route already
crosses the border toward the next pickup. Fuel prices alone must not introduce
an international border crossing.
Manual previews and saves enforce the same country guard. Retained invalid saved
choices remain editable but cannot display valid purchase-dependent balances or
be saved until the station occurrence is corrected.
If no eligible post-delivery quote exists, price coverage is unknown, not proof
that the mandatory trip is impossible. Use a reserve-only terminal policy: the
greater of half the physical tank and reserve plus the configured poor-area or
after-delivery buffer, whichever buffer is larger. Do not invent an exit station,
access road, border crossing or replacement price. Set the economic target to
that safety floor; unknown future purchases are excluded from cost valuation and
identified in the plan reason. Current-trip fuel and per-stop arrival estimates
remain available only if actual starting fuel and priced route purchases can
satisfy the full reserve-only policy.

Station access is a separate planning allowance: zero within 0.05 mile, otherwise
the larger of 0.5 mile and 1.5 times geographic distance, in each direction.
Nonzero round-trip time uses 30 mph plus two minutes. This is not a guaranteed
upper bound: divided highways, rivers, ramps and truck access remain unverified.
A forty-mile tolerance permits an off-road starting GPS position. Its
one-way allowance is consumed before the first purchase, without changing saved
road geometry or lowering the original first-arrival reserve requirement.
A position at the end of the current leg retains that mandatory stop as a
zero-mile anchor, then uses the unchanged following saved legs. Arrival does not
mark pickup or delivery complete. Nearby facility access is still charged once;
an access estimate does not certify an actual truck road connection.
Do not change saved road mileage, map geometry or live ETA to represent access.
A transient schedule preview can add estimated access miles/time to the mandatory
legs without storing that synthetic timing route or populating live ETA caches.

## Economic selection

Keep at most 24 occurrences, reserving a complete access-aware reachability
backbone before selecting price/access alternatives across all route sections.
Start with the cheapest normalized economic-price tier and admit dearer prices
until a complete estimated chain is possible. Retain competing near-road and
economic chains: the first feasible cheap tier is not automatically the winner.
For equal estimated access within a route section, retain the lower normalized
price first. Input order must not let a dearer roadside station consume the
section's near-road shortlist slot.

Local optimization includes both sides of every access, reserve, terminal policy,
small bridge purchases and useful fuel after delivery. Memoize identical subsets
per request. Compare up to 12 complete chains locally, with zero fuel routing
requests. There is no fixed charge per purchase stop; retain the fleet hourly cost.
Compare normalized USD/US-gallon prices under one IFTA preference; map colors do
not determine selection.

For equally ranked schedule feasibility, each additional purchase stop must save
at least $20 against a feasible alternative with fewer stops. Exactly $20 qualifies;
two additional stops must save at least $40. This is a selection threshold only:
purchase costs, economic costs and reported savings do not include a fictional fee.
Compare the complete horizon, including access fuel/time and useful final fuel,
after re-optimizing quantities. Keep single-stop omissions of the economic seed
alongside the cheapest and fewest-stop chains within the existing bounded search.
Reserve, capacity and arrival requirements still apply; necessary range purchases
and better schedule feasibility are not rejected by the threshold.

Charge purchases (including access consumption) and estimated access driving
time. Charge further schedule rest/wait once, above already priced driving delay.
A small discount must not win when extra fuel and time cost more. There is no
cumulative 30-minute/40-mile rejection. The forty-mile geographic search boundary limits
where unverified access estimates are used, not total itinerary detour. Legacy
detour settings remain readable for configuration/signature compatibility.

Protect against newly introduced cycle shortage and newly missed appointments,
and prefer known schedule data before comparing cost. Delay at already-late stops
has monetary cost rather than unconditional priority over savings. Use one HOS/
history snapshot, never an assumed future reset. Unknown history stays unknown;
saved schedule impact is dated estimated information, not live HOS authorization.

## Quantities and arrival policy

- Skip expensive fuel when a cheaper station is reachable with reserve; otherwise
  a small bridge purchase can reach the cheaper main fill.
- In a known non-poor destination area with an identified exit station, do not
  follow a selected cheaper station with a dearer optional purchase when filling
  at the cheaper station covers the entire calculated horizon and arrival minimum
  (including post-delivery exit fuel and reserve). Test the full capacity rather
  than the selected quantity so a partial fill cannot evade this rule. Poor or
  unknown areas, required range purchases and later cheaper stations retain their
  existing evaluation. This is an explicit stop-avoidance policy, not a fixed fee
  or a claim that the skipped stop has no theoretical terminal-value savings.
- A selected `Fill up` reaches the exact configured tank target, including fractional
  gallons. Charge the fraction once and carry it through later quantities and
  terminal value. Do not floor twice or merely change a 99% label to 100%.
- Carry continuous consumption and starting fuel through every leg. Do not ceil
  consumption separately per leg: equal-distance alternatives must not acquire
  different purchase deficits solely because a station is at a different mile.
  Check the retained actual balance against reserve and charge the complete purchase.
- Automatic purchases are at least 25 US gallons, then 30, 40, 50 and so on;
  the exact configured full-tank amount is also eligible when it is at least 25.
  Evaluate these volumes before ranking costs, not as a display-only rounding step.
  Never round a full purchase above capacity. Price savings must still cover
  extra fuel and time. Economic top-ups above 80% still need 25 gallons of headroom.
- Bound continuous alternatives to four labels per whole-gallon balance band:
  cash cost, replacement-valued cost, and range alternatives (including fewest-stop
  ordering). Retain unrounded balances and prices in every label. This and the
  candidate/chain limits make selection a bounded local search, not an exhaustive
  global-optimum guarantee. The final score uses the retained actual arrival fuel.
- The first purchase may be reached below reserve from any starting level,
  provided estimated arrival is nonnegative. Warn about the reserve shortfall,
  then restore reserve; later legs and final arrival keep normal reserve. If no
  station is reachable, show the nearest priced candidate with the additional
  fuel required. This access warning is separate from a feasible fuel plan and
  cannot create purchases, cost totals or a driving recommendation. A truck
  already at the pump can refuel with an empty tank.

Post-delivery fuel is estimated without provider requests, using local prices and
the saved onward direction when one exists. Preserve the configured buffer and at
least half the physical tank in expensive/poorly served regions; a longer estimated
escape may require more. Value useful remaining fuel at normalized replacement
price so an empty arrival does not look cheaper by omitting later fuel.
Value useful fuel up to the configured fill limit even in well-served regions.
The minimum arrival reserve is a safety floor, not the economic valuation ceiling:
an already-required cheaper stop may fill fully when post-delivery replacement is
more expensive. Retain partial purchases when later fuel is cheaper or equal, and
include access and stop costs when deciding whether an extra stop is worthwhile.

## Persistence and projection

New snapshots declare `EstimatedStationAccess=true`, store only unchanged
`BaselineRoute`, and leave `CheckedRoute` null. Each visit keeps baseline
`RouteMilesAhead` separately from `MilesAhead`, which includes prior estimated
access and arrival at the station. Detour miles/time remain explicit. Mandatory
stop boundaries and GPS matching use baseline mileage, never accumulated access.

Atomically save the compact truck-owned itinerary, purchases, signatures and
baseline with the current route compatibility copy. Future fuel geometry must
not replace the current dispatch road or mileage. Failed writes retain prior
data; normal reads independently reject invalidated or unsafe recommendations.
Before saving, compare a fresh complete itinerary signature with the original
capture and recheck included assignments/stops. This catches changes during
automatic searches and manual edits even without cache invalidation. The early
comparison precedes the result transaction. The shared publication guard then
compares canonical work, historical connection batches and captured saved-road
versions inside the owned transaction before writing. Existing replacement
revision guards remain required. Road versions and historical selection seeds
persist for later reads and automatic refresh. The latter preserve lookup batch
membership and accepted signatures without copying predecessor geometry.
Validation replays relevant batches together with compact road-version checks
inside one read snapshot, including when the fuel summary cache is warm.
Historical corrections require refresh even when saved roads are unchanged.
An incoming connection stops influencing remaining fuel after the first visit
of its load is behind the current next stop. Completed earlier blocks are also
excluded. Missing legacy evidence requires refresh while retaining saved choices;
manual plans remain excluded from automatic replacement. Operational captures
are not accounting evidence.

Persistence retains the root execution scope only on that dispatch's stop block;
subsequent legacy blocks retain their own null execution scope and zero execution
revision. All included dispatches have ordered, unique ownership, and mixed-scope
snapshots require each dispatch's signature. Any onward terminal hint belongs to
the last legacy block, never to a native transfer boundary or an included load.

Projection is provider-free. Match fresh GPS to the saved leg, trim visits by
baseline coordinates and rebase from the latest valid tank observation. Charge
access in and out separately, and update full buys to the exact target. Passing
a station is not proof of purchase. Reads do not mutate the original snapshot.
Keep global visit numbers after load completion and for return visits.
Publish per-mandatory-stop arrival gallons and percentage with the valid projected
fuel plan. Consume saved baseline distance and estimated access once, and credit
only remaining purchases owned by that mandatory leg or earlier legs. Future-leg
purchases must not increase pickup arrival fuel. Rebase from the latest accepted
starting balance and clear these estimates when the plan cannot be validated.

Normal reads retain legacy checked versions 11–20, using their declared
geometry basis; missing road evidence now requires recalculation before reuse. Estimated-access snapshots require version 28 or later because
older searches did not constrain access to the matched road's country. Preserve
those older saved choices for recovery, but mark them for recalculation and do
not publish their recommendations or purchase-dependent arrival estimates.
Automatic selection searches the saved baseline again rather than locking onto
the old fuel chain. Removing the fixed stop charge changes the fuel-settings
signature, so affected saved fuel plans need recalculation; it does not change
the mandatory road input hash.

When no valid purchase plan is available, current-stop arrival estimates may use
the latest valid timestamped fuel reading and remaining saved road mileage
without crediting purchases. They still require unchanged route inputs and fresh,
valid, on-route GPS progress. Do not assume zero progress when GPS is missing,
or show the current tank as historical arrival fuel at passed stops. A previously
validated manual starting-fuel projection does not require a telemetry fuel
reading. Valid projected access estimates remain available at off-road facilities
or stations; off-route positions cannot create a road-only fallback estimate.
Reading age alone does not invalidate the latest reported level.

## Editing the current plan

The map editor changes the same truck-owned plan as automatic selection. There is
one current snapshot, no revision history, and no BVD transaction matching yet.
An edit specifies the complete remaining visit order, partial quantities, or an
exact configured full-tank target. Manual slider choices use five US-gallon steps
starting at 25 gallons. Unchanged legacy partial
quantities may retain their original precision. Stop identity includes the mandatory
leg so outbound and return visits remain distinct.

The editor presents fixed pickup/delivery anchors and movable fuel visits in one
timeline. Pointer dragging or keyboard arrows change the visit order and its
mandatory-leg placement together. Selecting a visit highlights its existing price
marker and pans it into the unobscured map area without changing zoom. Quantity
edits do not repeatedly recenter the map. Purchase cost is provided by Application
in normalized USD; the Client only formats it.

Each preview edit returns a server-calculated purchase limit from that visit's
arrival balance and configured fill target. Incoming limit values are ignored.
Unresolved or physically unknown balances return no limit; an excessive current
purchase still returns its corrective limit, but cannot establish downstream
headroom. The slider maps its final endpoint to
the exact full-tank target, rather than offering the whole tank capacity as a
purchase at every visit.

For the selected visit, the server prepares bounded quantity choices with the
downstream balances, costs and validation errors. An increase is absorbed by
reducing later partial purchases in order, down to 25 gallons per visit. A decrease is
absorbed by increasing later purchases within capacity. Later Full tank visits keep
their target mode: replay buys the exact headroom after earlier changes, including
fractions and amounts below the 25-gallon partial minimum. Reaching that target
absorbs the balance difference before later visits; the target flag survives
preview, saving and reopening. Normal replay reserve and minimum-purchase checks
still apply. Unabsorbed surplus stays
at the finish; no visit is silently removed. For example, 25/100/50 can become
35/90/50 without a new search or slider HTTP request. Client only copies the
prepared values. Changing the selected visit, station or order obtains a new
table; mismatched/pending choices disable quantity controls. Older API responses
without choices use the cancellable preview compatibility path.

Preview does not write either saved copy or call routing providers. Application
replays the ordered quantities with continuous saved-road/access consumption,
as normal projection does. Automatic purchase steps do not rewrite saved manual
quantities. Newly edited partial purchases require at least 25 gallons; unchanged
legacy values retain their original precision and replay validation. Automatic
selection retains ten-gallon upward rounding. Invalid drafts retain their editable rows
and actionable errors. Unresolved prices or station occurrences omit calculated
values; unsafe but calculable drafts display their balances and cannot be saved.
Only verified unchanged remaining road geometry may silently trim passed visits
while opening the editor.

Saving validates the current load, all assigned stops, effective settings and the
opened snapshot timestamp. Atomic compare-and-replace and the route-copy transaction
reject concurrent stale writes. A saved manual plan is protected from automatic
replacement until the user explicitly confirms reset to automatic selection.
Normal projection still rebases observations; it is not confirmation of a purchase.
Remaining cost includes remaining purchase and estimated access-driving cost,
but does not charge historical schedule waiting again.

Summary and geometry bounds remain 512 KiB and 8 MiB. Fuel memory accounts at most
8 MiB, one requested leg/version per truck, with 30-minute leg and 30-second price
signature expiry. Limit cold geometry reads and manual searches to two concurrent
operations each per process; serialize searches per truck. These are accounting
limits, not measured process-memory guarantees.

The request geometry index retains at most 2,048 plus leg-count coarse blocks and
refines against original saved points. No candidate road responses or losing
full-route variants are fetched or retained. Provider body/point/timeout limits
continue protecting ordinary route preparation outside fuel calculation.

## Presentation and imports

Keep eligible complete values during background refresh without internal lifecycle
messages. Fuel cards retain numbered visits, `Buy`/`Fill up`, purchase quantities,
distance and separate `On arrival`/`After fueling` gauges. Gauges use physical
capacity; absent values are neutral, not invented zero. Compact `Fuel N` order
badges are distinct from load-stop circles; no distance labels appear above stations.
Return visits have separate numbered popup
cards. Price colors, selected-station rings and pickup/delivery circles are separate.

BVD reads US `STATE` and Canadian `PROV`/`PROVINCE`, with trimmed case-insensitive
headers. Reject missing/conflicting regions before lookup or replacement. IFTA
uses jurisdiction/currency. Map quotes use road diesel, not reefer/other products;
cash/IFTA views select ready quotes independently of the optimizer's discount list.
Ambiguous currencies stay neutral without a truck exchange rate.

## Oregon diesel comparison

Without a published Oregon diesel IFTA rate in USD, compare the discounted price
unchanged (zero per-gallon deduction). An available published rate takes precedence.
Missing rates elsewhere remain unknown. This fleet rule does not remove Oregon
weight-mile tax or calculate reporting liabilities.
See [Oregon interstate operations](https://www.oregon.gov/odot/MCT/Pages/Interstate-Operations-IFTA.aspx).
