# Route and fuel memory audit — September 9, 2026

## Scope and status

Analysis and local optimization of route loading, fuel search, provider caching,
saved-plan validation, and map-related memory ownership. This change has not been
deployed. No server memory limit, production data, fleet fuel setting, or migration
was changed. No live fuel search was triggered for this audit.

The observed incident combines request-lifetime retention and allocation peaks.
An indefinitely growing managed-heap leak has not been demonstrated. A bounded
cache is not a bound on the complete process or concurrent request allocations.

## Operational evidence recorded before changes

Production revision `amftms-api-00087-fg7` exceeded its 512 MiB limit with 520 MiB
reported at 20:16:15 and 20:22:29 UTC. Truck 11005's manual fuel calculation for
dispatch `5269044d-c35c-4977-8023-92eedcbcf62d` also failed final snapshot validation.
These observations are not a heap dump establishing each object's contribution.

A scoped read-only inspection found 11 checked alternative routes during the
20:21–20:22 calculation, each approximately 4.3–4.6 MB of serialized JSON and
49,000–52,000 coordinates in each of the duplicated flat/leg representations.
The ordinary saved itinerary, before adding fuel detours, contained:

| Saved segment | Leg points |
| --- | ---: |
| Current load 1370 | 24,148 |
| Connection to 1377 | 1,791 |
| Load 1377 | 12,527 |
| Connection to 1379 | 2,067 |
| Load 1379 | 11,811 |
| Total | 52,344 |

The current saved route includes an already-passed prefix. These are stored point
counts, not counts of objects simultaneously resident in the production process.

Load 1377's saved delivery road endpoint was 2.61 miles from the current confirmed
stop. Checked alternatives reached the confirmed stop, so they disagreed with the
baseline. This mismatch is not a permitted fuel detour or a reason to relax final
validation. Another legitimate road snap measured 0.08044 mile, which is why the
existing 0.5-mile facility tolerance remains distinct from the 0.05-mile join limit.

## Findings and implemented changes

1. **Completed provider payloads remained tracked for the entire request.** Each
   checked route added a `RoutingApiCall` whose multi-megabyte `ResultJson` stayed
   in the shared EF context. The provider now detaches only its own audit entity
   and clears its payload reference on every exit. It does not clear unrelated
   tracked changes. Committed reservations, daily/minute budgets, failure cooldowns
   and cancellation semantics remain covered by tests.
2. **HTTP parsing created unnecessary whole-response copies.** Successful replies
   now use headers-first streaming and JSON stream parsing, with the configured
   timeout covering the body. Non-success replies do not read the body. A declared
   or actual response above 16 MiB is rejected; cumulative geometry above 200,000
   points is rejected before coordinate objects are created.
3. **Coordinates were serialized twice.** New provider, base-route and deadhead
   cache writes store leg coordinates once. The provider restores its full flat
   return contract. Private versioned request hashes protect older rolling/rollback
   binaries; valid legacy successes and cooldowns remain reusable. Cached JSON has
   a 32 MiB UTF-8 bound and a point-count preflight before object expansion.
4. **Fuel search allocated a detailed segment index unnecessarily.** A shared
   request-scoped `FuelSearchGeometry` uses at most 2,048 plus the leg count coarse
   blocks, with conservative bounds and exact refinement of nearby original
   segments. Region lookup and station occurrence matching share it. Original
   geometry remains resident; mileage, time, directions, repeated visits and
   mandatory stops are not replaced with straight-line estimates. The generic
   exact GPS index also no longer creates a temporary per-segment length array.
5. **Old saved geometry was loaded before checking eligibility.** Explicit fuel
   calculations now check the compact summary first. Ineligible version, assignment,
   completion, price or profile data do not load the full saved checked/baseline
   geometry. Unsuccessful reuse is released before a new search.
6. **Obsolete cache versions occupied space until expiry.** Route display caching
   now retains one revision per dispatch; fuel geometry caching retains one leg
   per truck. Older requests cannot replace a newer saved fuel version. Each cache
   allows at most two simultaneous cold geometry loads; hot reads bypass these
   limits. Cancellation and load failures release the slots.
7. **Read-cache generation identities had no count limit.** The catalog now has
   a 4,096-entry LRU bound with a monotonic eviction epoch. Forgotten identities
   cannot revive old cached data or publish a stale in-flight result.
8. **Misanchored saved routes failed too late.** Base/deadhead reuse now checks
   facility endpoints and continuity. Only contiguous invalid base leg ranges are
   repaired. Fuel horizons validate their segments and joins before candidate
   search. Fresh invalid replies fail before publication. Final truck fuel storage
   validation was not loosened.
9. **Future-load assembly copied and retained unnecessary geometry data.** Base
   reads are now untracked; owned writes detach and release their JSON afterward,
   without touching caller-owned pending work. Joining loads reuses the original
   legs rather than copying the flat path each time. A cumulative 200,000-point
   itinerary bound fails before search, not only at final snapshot persistence.

The 24-candidate/12-road-check limits, two manual searches per process, all-assigned-
loads horizon, reserve, capacity, $20 stop cost and extra-time economics remain.
No UI messages, marker colors, gauges or ETA retention behavior were changed.

## Local measurements

The focused report is
[`route-memory-final-2026-09-09.trx`](../../../artifacts/memory-audit-2026-09-09/route-memory-final-2026-09-09.trx).
Fixtures are synthetic and provider tests use isolated in-memory SQLite and fake
HTTP responses, not the application or production database.

| Check | Result |
| --- | --- |
| 11 routes, 48,001 flat points each: equivalent legacy serialized strings | 93,588,524 B (about 89.3 MiB) |
| Same routes, compact persisted strings | 46,797,274 B; about half the serialized size |
| Tracked routing payload after every completed check | 0 B; unrelated tracked entity preserved |
| Additional search index over 50,002 leg points | 2,000 blocks; 144,312 allocated B |
| Additional search index over 100,002 leg points | 2,042 blocks; 147,336 allocated B |
| 40 station matches on those fixtures | 1,280 allocated B; at most 100 / 147 original segments refined per match |
| Exact GPS index over 50,000 segments | 2,400,128 allocated B, without a temporary length array |
| Joining two loads containing 50,002 leg points | 344 allocated B; original legs shared, no flat path copy |
| Metadata-only display read, small or larger display geometry | 2,680 allocated B per read |
| Full larger display read in the same fixture | 117,680 allocated B per read |
| Replaced display snapshot | Collectible before its original expiry |

Serialized string totals exclude object overhead. Index figures exclude the
original input route. Allocated bytes are not retained heap or process RSS. The
legacy string figure describes the old payload representation; it is not a replay
of the old production binary under load.

## Verification

- Initial affected run: 898/905 server tests passed, 284/284 Client C# tests passed.
  Seven existing next-load tests had stored pickup-to-delivery coordinates as their
  deadhead fixture. Correcting the fixture to previous-delivery-to-current-pickup
  preserved all original ready/pending, queue, cache and provider-call assertions.
- An intermediate full run passed 990 server checks before the final base-tracker
  and join protections were added. Three newly added sequential-load fixture cases
  initially reused load number zero and hit its unique constraint; distinct fixture
  load numbers corrected that setup without changing assertions.
- Final `bash test.sh all`: **997 server, 485 Client C#, 213 JavaScript tests passed**,
  with architecture checks and warning-as-error builds included.
- Final focused no-build measurement rerun: **45/45 passed**, with the TRX linked above.
- Boundary checks include round trips, high latitudes/date line, mandatory and
  zero-length boundaries, exact matching, cancellation, old/new provider cache
  formats, stream timeouts, oversized response/cache input, tracker isolation,
  cold-load limits and stale-generation replacement.
- No PostgreSQL execution fixture was available; those checks were not run.
  No migrations were added or applied. No authenticated browser heap soak,
  production heap capture, live 11005 recalculation or post-deployment memory
  measurement was performed. The local development servers were not restarted.

## Remaining limits and follow-up observations

Reviewed payload accounting limits total about 112 MiB across read cache (32),
route display (32), fuel geometry/prices (8), HOS history (16), telemetry stream
(16), and one fleet preview (8). This excludes object overhead, shared general
memory-cache entries, runtime memory, pooled buffers and requests in flight.

Cold fuel projection still reads the validated baseline/checked storage blob to
derive one leg, now with bounded concurrency. Cached provider JSON is materialized
once as a database string before its object-expansion check. Array pools may
retain returned buffers. These are remaining allocation costs, not evidence of
an indefinitely accumulating route collection.

Separate source-only findings: all-fleet preview results and paginated telemetry
can be materialized before their cache-admission bounds. They warrant their own
realistic fleet-size measurements; they were not attributed to this fuel incident
or changed in this patch. ETA metadata dictionaries are pruned through the active
worker's ten-minute inactivity policy rather than a byte ceiling.

The reviewed Client releases old route/station arrays, listeners, animation work,
GPU resources and interop references on replacement/disposal. It intentionally
retains bounded current/next-load payloads and up to 300 playback points per truck.
No accumulating historical route collection was found in those sources. That is
not a substitute for a browser heap soak or proof that all application leaks are
absent.

After deployment, verify one controlled 11005 calculation and repeated ordinary
map reads against container memory and failures. Confirm that memory returns to
a stable post-GC range; do not infer a production memory ceiling from these tests
or compensate by silently increasing the server's RAM limit.
