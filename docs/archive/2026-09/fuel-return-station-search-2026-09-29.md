# Return-station alternative search

## Contract and evidence

A station substitution is not an additional purchase stop. A return visit to
the same station remains a distinct occurrence, keyed by station and visit leg.
The existing quantity optimizer, arrival-date pricing and schedule comparison
must assess a proposed substitution; lower pump price alone cannot select it.

Root inspected saved diagnostic output for truck 54777 at 01:45:17 UTC:

- Calculation: 2026-09-29 01:42:08 UTC, assignment revision 9.
- Work: AMF1413 followed by AMF1405.
- Purchases: #706 at $5.52, #333 at $5.30, #305 at $5.579 per gallon.
- Selected score: $2059.88; #706 + #333 scored $2071.10.
- #706 + #333 + returning #706 + #305 was rejected as containing a
  station without a useful purchase.
- #706 + #333 + returning #706 was absent from the saved checks.

The preceding 21:50 calculation had different inputs and two #706 visits.
Its $1.84 comparison against #497 does not explain the newer plan.
Evidence was read from the existing managed diagnostic runs `BanuCn` and
`w2FZOC`. These are saved observations, not a fresh production read or a
complete optimizer replay. No production state was changed.

## Source finding and local correction

`FuelZoneSearch.Schedule` appended an unrepresented zone candidate to its
best scenario. It did not propose replacing an existing purchase. The
access-aware `FuelRouteSearch.Chains` branch does not enumerate all station
substitutions either. Rejecting an appended four-stop chain therefore does
not establish the cost or feasibility of the three-stop substitution.

The local correction schedules one same-leg substitution per unrepresented
zone before appended alternatives. It replaces the closest existing purchase
on that visit leg, preserving every other visit. Occurrence keys keep outbound
and return visits separate. Existing append alternatives remain available.
No station numbers or truck identities occur in the implementation.

The owner remains the Domain search scheduler. `FuelPlanningService` and
`FuelChainComparison` retain pricing, quantity, schedule and publication
ownership. Fleet Map and Dispatch still consume the shared saved result.
No queries, provider calls, caches, persistence schemas or publication guards
were added. At most `ZoneRoadChecks` extra scenario lists are constructed
(configured maximum four). The existing final comparison budget is unchanged;
new alternatives can displace later alternatives within that budget. This is
a bounded search improvement, not proof of a global optimum or measured cost
reduction. Equivalent scenario deduplication remains in the existing owner.

Selection version 34 lets the existing ordinary automatic-refresh owner
reconsider older automatic snapshots after an authorized release. Manual
snapshots and handed-over station retention remain governed by their existing
rules. There is no forced production refresh, reset or send.

## Verification and open incident

The Domain project builds with zero warnings and errors. CSharpier formatted
the changed files. No tests, harnesses or production recalculations were run,
following the user's retained restriction. The user explicitly authorized deployment without the requested focused
verification. It remains not run; this is not red/green regression evidence.

Before release, the focused regression should show the three-stop replacement
missing on the old scheduler and present on the correction, preserving the
outbound #706 occurrence and final comparison bounds. Quantity/cost replay
must use a coherent captured input set and compare the replacement against
the selected chain. Also check unrelated visit legs, already represented
zones, reserve rejection and the fewer-stop alternative.

There is no runtime auditor proving that every cheaper substitution was
considered: doing so would repeat optimization. Fuel owns this coverage gap;
completion requires a focused regression and a bounded normal-owner read
after an authorized release. No exact saving or corrected production plan
is claimed. The incident remains open until its normal-owner result is observed.

## Authorized deployment

The user authorized publication without tests or the proposed replay. Root
performed the deployment directly; no assistant was used for this release.

- Source: `47d61210`, based on frontend release `649530b0`.
- Before the patch, `Server/` matched deployed API source `a596a667`.
- Cloud Build: `c1884ab9-213d-4e47-8745-2b77d59511ae`, SUCCESS.
- Build-only Docker configuration; no Node, .NET or browser tests ran.
- Image: `sha256:e42b50eecfed11547457edf14f65be5444d0d993ca8c486c1166d4ec782797fc`.
- Revision: `amftms-api-b-c1884ab9-213d-4e47-8745-2b77d59511ae`.
- Ready/digest identity confirmed before switching 100% traffic.
- Requested and observed traffic matched on 2026-09-29 around 02:05 UTC.
- Previous revision became Active=False/Retired at 02:05:40.914691 UTC;
  TrafficShutDown=True at 02:05:40.988150 UTC.
- No schema change, data repair, forced calculation, send or frontend deploy.
- Existing revision-specific message-send hold was not released.
- Build/traffic receipts retained in managed `diagnostic-hD7moI`.

Deployment is confirmed; the exact 54777 replacement cost and new saved result
were not checked, as explicitly requested. No production incident closure or
minimum-cost guarantee is claimed.

## Follow-up: version 34 still omits the return alternative

A scoped read-only production read at 02:39 UTC found a newer automatic plan
calculated at 02:36:30 UTC, assignment revision 10, selection version 34.
It still selected #706 / #333 / #305; #706 / #333 / returning #706 was absent
from its recorded comparisons. Deployment alone did not resolve this incident.
The snapshot and a separate arithmetic comparison are retained locally in
main checkout managed diagnostic `1LGMvt`.

The first correction operated only on geographic representatives (three under
the saved policy), not the full selected candidate set. Its global membership
check also suppressed replacement whenever a candidate appeared in any seed.
The access-aware chain search returned after omissions without generating
same-size substitutions. These are incomplete-search paths, not evidence that
an omitted alternative is uneconomic.

The revised contract is local and bounded: for each selected occurrence, compare
its nearest same-leg substitution in two fixed nonempty scaffolds, the best
estimated-cost chain and the nearest-access chain. Preserve all other visits,
including an earlier visit to the same station. Optimize quantities and reserves
through the existing Domain owner before cost ordering and final scheduling.
An occurrence already in that scaffold is skipped, not one in an unrelated seed.
Geographic representation is no longer a prerequisite for these substitutions.
The previous zone-specific replacement loop is removed rather than duplicated.

This adds at most twice the selected candidate count in optimization attempts:
48 under the saved policy, at most 64 under the configured shortlist maximum.
Existing operation-local evaluation keys coalesce duplicate chains. Scaffolds
are captured before expansion, so this is not recursive search. No additional
provider or database reads, persistent cache, background worker or final schedule
comparison slots are introduced. Actual additional CPU time is not measured.
The bounded final scheduling budget and initial candidate shortlist can still
exclude alternatives; this does not establish global optimality.

### Cash purchases and residual fuel are different objectives

Using the saved baseline geometry, the return #706 occurrence projects to
mile 778.596, approximately 0.104 miles from the road. A separate arithmetic
comparison assumes the saved #706 price of $5.52 applies on return, the same
one-mile/four-minute access at all three visits, and full tanks at the second
and third visits. It does not replay appointments, HOS or arrival-date pricing.

- Returning to #706: approximately 42.312 gallons at the third stop, total
  cash purchases $1589.50, approximately 170.625 gallons at the finish.
- Saved #305: approximately 53.981 gallons at the third stop, total cash
  purchases $1657.09, approximately 182.294 gallons at the finish.
- The saved policy values future fuel at $5.843 per gallon toward a 250-gallon
  target. It charges approximately $463.79 versus $395.61 respectively.
- Cash savings at #706 are approximately $67.60, but its extra future-fuel
  charge is approximately $68.18. On these assumptions, #305 is approximately
  $0.58 cheaper overall, before equal access charges and any schedule changes.

Therefore the cheaper pump price does not prove that #706 must win the existing
objective. The fix must compare it, not force a station or silently change the
terminal-fuel valuation rule. The displayed screenshot and this later saved
snapshot have slightly different totals; these figures describe the captured
02:36 calculation only.

Selection version 35 requests normal refresh of older automatic results through
the existing owner. Manual and handed-over rules, assignment/tenant guards,
commit ordering and late-result rejection remain unchanged. Tests, comparison
harnesses and provider replay remain not run at the user's instruction. Required
future coverage includes return occurrence preservation, alternatives present in
other seeds, same-leg isolation, both scaffold paths and bounded work counts.
The existing runtime-optimality coverage gap remains open; final closure requires
a normal-owner result after publication, not this source change or build.
