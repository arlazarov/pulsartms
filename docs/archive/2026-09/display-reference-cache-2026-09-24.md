# Display reference cache: built, measured, reverted (September 24)

Local only, on the isolated load fixture (ten trucks, one CPU, 1 GiB,
synthetic roads of about 75,000 points each). Nothing was deployed.

## What was tried

`593a8857` kept a load's parsed base road (the display reference) in
`RouteDisplayCache`. The entry was keyed by row and a new `Revision`
column (migration `AddDispatchBaseRouteRevision`). The read stayed one
round trip, and it left out the road's JSON when the kept copy had the
row's revision. Regressions covered the call count, a write between the
lookup and the read, late results and both writers. Each was checked by
reverting its guard.

## Measured

The same prepared fixture was used throughout, idle, with four traced
windows alternating the build before (`5bf92f65`) and after. Allocation is
sampled bytes over the traced window after 60 s of startup. CPU is the
container's `docker stats` average.

| Window | CPU % | Allocation MB | `ReadReferenceAsync` MB | Chunk decode MB | Plan load MB |
| --- | ---: | ---: | ---: | ---: | ---: |
| before 1 | 14.3 | 1,253 | 179 | 166 | 318 |
| after 1 | 13.4 | 1,363 | 180 | 236 | 438 |
| before 2 | 13.4 | 1,317 | 214 | 166 | 322 |
| after 2 | 13.7 | 1,443 | 231 | 236 | 447 |

- **The cache never helped.** The reference path allocated as much after
  as before, and the base road was parsed as often.
- **It made plan reads worse.** Chunk decoding and plan loading rose in
  both "after" windows.
- **CPU did not separate** between the builds.
- **Foreground planning reads were unchanged.** `compare.py` measured one
  cold and one warm run of each build: planning reads 5–360 ms, and the
  fuel and ETA calculation 8–11 s, both dominated by other work.

## Why

The whole route-display budget is 16 MiB (`CacheBudgets.RouteDisplay`).
One of these roads, kept parsed, is about 3.6 MB at the entry's own
estimate, so ten cannot fit. The references evicted each other and the
plan snapshots that share the budget, so every read missed and parsed
again, and plans were decoded again. Real roads are shorter, but nothing
measured shows a gain anywhere. The change was reverted, migration
included.

## What would remove the cost

The re-read happens because a plan from the truck's position can be
saved without its reference stops: it was built before the base road
existed, or while the road was incomplete. Every later read then looks
for the reference again and never keeps it. The fix belongs in the
writer. When the base road appears, the plan should be given its
reference once, through the plan's owner, under the publication guards.
That changes stored geometry, and possibly the geometry revision that
fuel's saved-road validation compares, so it needs its own review and a
controlled-interleaving test. Not started.

Evidence: `artifacts/managed/diagnostic-ezKDoN` (pinned; `idle-*`
windows and reports, `compare.txt`).
