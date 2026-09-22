# Route compaction characterization, September 21

The revised plan is in
[the fleet efficiency guide](../../architecture/fleet-efficiency.md).
This work adds characterization tests and design gates, not a new production
matcher, route schema or migrated history.

## Results

- A synthetic straight corridor with 20,001 points simplifies to two display
  endpoints without modifying the input list. It is not a sampled real I-80 road.
- Display compaction preserves original leg miles, seconds and stop boundaries.
- An uneven synthetic sequence of small bends demonstrates that retaining total
  mileage while reweighting simplified geometry can shift intermediate progress
  by more than two miles on a ten-mile fixture. This deliberately exposes why
  display simplification must not replace calculation geometry directly.
- Constructing 100 independent indexes over the same straight fixture allocated
  48,021,656 bytes with 10,001 points per index, versus 18,424 bytes for two-point
  indexes. Inputs and simplification were prepared outside the measured window.
  Both representations agree on progress for this straight fixture only.

These are index-construction allocations, not retained heap, container memory,
100 independent real routes, sustained throughput or production savings. They
justify further work but cannot establish a safe simplification tolerance.

## Verification

`bash test.sh routing` passed 1,213 server tests, 193 Client tests and 64
JavaScript architecture checks. A subsequent focused run rebuilt and passed all
four new characterization/allocation tests, with detailed allocation output.
Evidence is pinned in managed runs `diagnostic-Zi65lb` and
`diagnostic-fM2fYP`. No full-suite claim is made for this documentation/test-only
change. No database migration, browser check or live provider load test ran.

Still required: cumulative-measure implementation, differential matching across
ambiguous roads, versioned storage, movement history integration, bounded shared
work/cache ownership and sustained end-to-end load testing. Existing local
history-cache corrections are separate work and remain unpublished.
