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

## The writer-side fix needs an owner's decision

**Traced on a fresh prepared fixture** (`diagnostic-bF4CtD`), with its saved
rows read directly:
- 9 of 10 plans are "from the truck's position" with no reference stops or
  route;
- each one's base road exists, is complete, and has the expected legs (2
  legs for 3 stops);
- the background pass built those plans before the base road was written;
- every read since attaches the reference in memory and never keeps it.

Fuel and ETA never read the reference road; only the display does.

**The choice.** Saving the reference is a change to stored geometry. Either
it is a road replacement or it is not:

1. **Display attachment.**
   - The plan's owner writes the reference once, when a matching base road
     is there, and keeps the plan's version, so no fuel refresh and no ETA
     re-key.
   - One geometry history row of a new "reference" kind; the geometry
     revision moves once, which closes the truck's open movement segment
     once.
   - Guarded by the plan version and the base road's input hash (compare
     and swap).
   - Needs a storage path that adds reference chunks without counting as a
     replacement. Existing rows recover on their next summary refresh.
2. **Replacement through the normal writer.**
   - Simple, but it bumps the plan version: every affected truck's fuel
     plan is marked for refresh and recalculated automatically (under the
     hand-over rules), ETA forecasts are re-keyed, and each write records a
     full geometry history row.
3. **Build the base road before a from-position build.**
   - New plans carry the reference from the start.
   - Each such build makes the base road call earlier (normally made by the
     background base road operation anyway) plus one reconnect call to
     join the reference to the truck.
   - Existing plans keep re-reading until their next rebuild.

**Recommendation:** 1, because fuel and ETA do not depend on the reference.

## Built: option 1, the display attachment

Taken as a routine design decision within the local scope, after this
trace of who reads what:
- **Geometry revision** is the driven road's. The movement recorder keys
  its observations to it and closes the open segment when it changes. The
  history (`ReadRoadAtAsync`) rebuilds a past road from the "route" splices
  only.
- **Plan version** is what fuel (`RouteVersion`), ETA (road plan id and
  version) and the Client's geometry acknowledgment follow.

A reference-only change needs neither, and no separate reference version
is needed on the server: the exact-geometry cache keys on the manifest as
well, and the display snapshot is invalidated after commit.

**Change:**
- `RoutePlanStorage.RecordChange` treats a manifest change that leaves the
  route ranges and measures as they were as reference-only. No version
  bump, no fuel refresh, no movement close, no geometry revision, no
  history row.
- The planning pass, the plan's own writer (`AdvanceAutomaticallyAsync`),
  gives a from-position plan without reference stops the load's complete
  base road, once. It saves through the same publication (truck work
  re-read under the lock), the plan row's compare-and-swap (`PlanJson` and
  manifest concurrency tokens) and after-commit invalidation.
- A base road stamp (id, input hash, calculation time) read with the road
  is checked again under the lock. A road rewritten meanwhile is not
  attached, and the next pass attaches the new one.
- No provider call is added. The rejected cache is not restored.

**Regressions** (`AutomaticPlanningReferenceAttachmentTests`):
- the attachment keeps version, geometry revision, history and route, and
  a plan read then makes no base road read (it made one before);
- a base road committed between the pass's read and its commit is not
  attached;
- a plan replaced meanwhile is not overwritten
  (`DbUpdateConcurrencyException`, the other writer's row stays).

Removing the storage rule, the stamp check or the attachment each fails
them.

**Measured on a fresh fixture** (`diagnostic-1hJ26N`):

| Rows | Windows | Allocation MB (134 s) | Reference read MB | Base road parse MB |
| --- | --- | ---: | ---: | ---: |
| 8 of 10 without reference | 4, both builds | 1,286-1,358 | 181-264 | 101-229 |
| all attached | new build | 893 | 0 | 0 |
| all attached | old build | 911 | 0 | 0 |

- **Existing rows recover on a truck's next planning pass.** Idle, the
  fixture sends no telemetry, so no pass runs and 8 rows stayed
  unattached. After 70 s of ticks and enqueues, all 10 were attached,
  every plan kept geometry revision 1 and version 1, the history kept its
  10 rows, and nothing was logged as a failure.
- **The cost moved.** The stored reference is decoded with each plan load:
  chunk decoding went from 175 to 223 MB and restore from 64 to 93 MB.
  The net saving is about 400 MB per window.
- **Container CPU** did not separate (11.0-15.8% across windows).

**Left:** a map that already acknowledged the plan's version gets metadata
only and does not see a reference that appears later. That was already true
of the read-time attachment; a reference version in the acknowledgment
would change it.

**Consistency auditor:** no runtime rule. A plan without a stored reference
is not invalid: the read path still attaches the reference in memory, so the
display is correct either way, only dearer. Existing rows recover through
the ordinary planning pass. The regressions above cover the invariant that
matters: a reference never moves the driven road, its movement, fuel or ETA,
and a changed road or a replaced plan is never overwritten by a late
attachment.
