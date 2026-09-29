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
