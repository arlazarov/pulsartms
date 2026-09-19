# Base-road input capture and publication — September 17, 2026

## Implemented

Standalone and historical base-road preparation now shares RouteWorkSnapshot
with deadhead history and historical fuel lookup seeds. The existing immutable
load/stop contract was moved out of the history-specific module without changing
its serialized fields. Common capture/projection code is reused by both readers.
No persisted fuel-history schema version change was needed.

Base preparation captures the supplied load, stop collection, routing dimensions
and supplied coordinates before its first asynchronous read. A caller changing
these objects during provider work cannot alter the in-flight calculation.
Deadhead preparation also retains a private profile copy.

Before saving, standalone base preparation reloads the current source itinerary
or accepted execution section inside the protected publication transaction.
Changed stops, assignment revision, route choice, missing/cancelled work and a
source acquiring accepted execution reject the stale write. Completed native
legs remain valid historical work. Legacy number-only truck resolution retains
its existing semantics. Commercial-only and fuel-preference changes do not
invalidate a geometric road.

Publication also compares the initially observed saved-road identity, input hash
and calculation time. A newly inserted or replaced road cannot be overwritten by
a delayed provider response. The previous saved road remains intact on rejection.
Provider requests remain outside the transaction.

## Verification

- Full suite: 2,683 Server, 1,014 Client C# and 561 JavaScript tests: 4,258 passed.
  Evidence: `artifacts/managed/diagnostic-4QVlMb/tests.log`.
- Strict migration-probe build: zero warnings/errors.
  Evidence: `artifacts/managed/diagnostic-ddxmxj/build.log`.
- PostgreSQL fixture passed, including independent writes during standalone
  and historical base calculation; prior roads were retained on rejection.
  Evidence: `artifacts/managed/diagnostic-k7rotU/postgresql.log`.
  The isolated fixture was removed after verification.

Regression checks cover caller mutations, destination/assignment/choice changes,
source-to-accepted-work transition, historical inputs, competing road writes and
number-only assignments. The existing base-route reuse test now persists its
assignment and destination changes before calculation; its reuse assertions are
unchanged. Uncommitted destination edits are no longer treated as accepted work.

## Boundaries

The common immutable contract is a calculation input, not an accounting record.
Algorithms still consume private mutable compatibility projections. Removing
Dispatch.ForExecution, completing transfer writer unification, durable source-road
delivery and per-truck publication revisions remain open in the core specification.
The conservative fifteen-table publication lock remains in place.

No production latency or contention improvement was measured. Authenticated
browser checks, deployment and working-database migration were not performed.
