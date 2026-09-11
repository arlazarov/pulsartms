# Route and fuel selection rules

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
Application uses a 250-US-gallon total tank capacity and fleet defaults of a 100% fill limit, a 25-US-gallon reserve
and $35 per driving hour, with zero fixed purchase-stop charge. Legacy saved overrides for these fields are
ignored when reading effective settings; reading does not rewrite stored settings.
Explicit settings saves validate the request before applying these defaults and
preserve the IFTA basis, legacy detour setting and exchange rate. A saved $20
stop charge is ignored by effective settings without rewriting the stored row.
Consumption and the remaining preferences come from the effective profile.

## Saved-route planning without fuel routing requests

Manual fuel calculation does not call routing/geocoding providers, rebuild roads,
repair connections or advance route tracking. Read the current saved road and saved
base/deadhead geometry for all remaining assignments. Ordinary route preparation
and tracking remain separate responsibilities.

Saved paths must match profiles, confirmed facilities, assignments and remaining
stop identities. Facility snapping remains within 0.5 mile and adjacent endpoints
within 0.05 mile. Missing, stale or disconnected geometry fails without replacing
the previous fuel result. Never substitute a straight line for a mandatory road.
More than 40 mandatory stops or 200,000 saved leg points fails without truncation.

Consider station occurrences within forty geographic miles of the saved road.
These are nearby candidates, not verified truck approaches. Preserve mandatory-leg
identity, including outbound and return visits to the same station. Original road
geometry and provider mileage determine along-route distance.

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
- A positive level below reserve may reach its first purchase with nonnegative
  estimated arrival, then must restore reserve. Later legs and final arrival keep
  normal reserve. Empty fuel or an unreachable first station does not produce a
  driveable recommendation; confirm fuel or arrange refueling.

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
baseline with the current route compatibility copy. Future fuel geometry must not
replace the current dispatch road or mileage. Failed writes retain prior data;
normal reads independently reject invalidated or unsafe recommendations.

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

Version 27 retains valid legacy checked versions 11–20 and estimated versions 21–26
on normal reads, using their declared geometry basis. Automatic selection searches
the saved baseline again rather than locking onto the old fuel chain. Removing
the fixed stop charge changes the fuel-settings signature, so affected saved fuel
plans need recalculation; it does not change the mandatory road input hash.

## Editing the current plan

The map editor changes the same truck-owned plan as automatic selection. There is
one current snapshot, no revision history, and no BVD transaction matching yet.
An edit specifies the complete remaining visit order, partial quantities in ten
US-gallon steps, or an exact configured full-tank target. Unchanged legacy partial
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
headroom. The slider uses ten-gallon partial steps and maps its final endpoint to
the exact full-tank target, rather than offering the whole tank capacity as a
purchase at every visit.

Preview does not write either saved copy or call routing providers. Application
replays the ordered quantities with continuous saved-road/access consumption,
as normal projection does. Automatic purchase steps do not rewrite saved manual
quantities; existing manual editing and legacy projection retain the 10-gallon
minimum. Invalid drafts retain their editable rows
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
