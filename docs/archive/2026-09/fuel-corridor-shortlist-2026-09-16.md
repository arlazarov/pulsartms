# Equal-access fuel shortlist correction

## Read-only production observation

Truck 11005's saved automatic snapshot, calculated at 06:17 UTC on September
16, selected Love's #706 at USD 5.996 per US gallon. Current September 16 quotes
included Love's #333 at USD 5.808, with an estimated one-mile round-trip access
on an earlier mandatory leg. The saved comparison considered only the later
occurrence of #333, with approximately 79 miles of estimated access.

The 24-occurrence shortlist selected each section's first minimum-access
candidate without resolving equal access by economic price. Its other slots
favored cheaper but distant candidates. The useful earlier #333 occurrence was
excluded before quantity and schedule evaluation. This is not evidence that
station colors alone identify the best complete itinerary.

## Local correction

Within each section, equal-access candidates now prefer the lower normalized
economic price. Reachability coverage, mandatory-leg identities, candidate and
comparison limits, reserve, terminal valuation and schedule ranking are unchanged.
Selection version 31 requests normal automatic refresh after deployment; manual
plans remain protected.

An isolated synthetic crowded-shortlist regression failed before the change and
passed afterward. It also covers reversed input order. Read-only reproduction
with current saved geometry retained the earlier #333 occurrence after the fix.
The diagnostic performs no writes or provider requests. An estimated two-stop
comparison is not a published recommendation or a verified station access road.

## ETA observation

The same truck's 11:42 UTC forecast on September 16 reported arrival at 08:04
Eastern that morning: three rounded driving minutes, zero rest minutes, fifteen
pre-trip minutes and five planned fuel minutes. September 16 was the current local
date. Those twenty minutes are planning allowances, not road travel time or proof
that the driver actually needs fuel before this nearby delivery. No ETA policy
change is included in this correction.

## Verification scope

The affected fuel runner includes dependent routing/ETA and architecture checks.
No schema migration, production write, deployment or production performance
measurement is included in this follow-up. PostgreSQL integration checks require
an isolated fixture and were not run against the application database.
