# Core rebuild phase 1I: canonical-work publication transaction

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, schema migration or application-database experiment was
performed.

## Delivered behavior

Final canonical-work validation and result persistence now share one owned
transaction. PlanningWorkPublication opens IPlanningPublicationScope, rereads
the captured itinerary at its original as-of instant, compares the complete
signature, and returns the same transaction to the writer. Validation failure
disposes the transaction before result writes. Calculation and provider/HOS
requests finish before entering it.

The boundary is used by:

- Route builds and automatic progress writes.
- Base-road writes invoked by a captured route build.
- Route-choice preview and save.
- Automatic fuel calculation and manual fuel editing.
- ETA forecast-batch publication.

Existing native leg ownership, expected result revision, route-choice version,
GPS, actor and draft-expiry checks remain. Fuel's route compatibility copy and
truck-owned plan still commit together. Route progress invalidates its read
cache again after commit, preventing a concurrent pre-commit read from
retaining an older cached value. ETA considers its memory result committed
only after the
database transaction commits; an injected commit failure leaves no unpublished
memory entry or partially persisted forecast.

FuelPlanningService no longer needs IAppDbContext directly. Its publication
lifecycle is owned by the shared boundary and its existing result stores.
No financial formula, routing policy, public HTTP field or Client behavior was
changed.

## Database protocol and trade-off

Infrastructure owns provider selection and SQL. PostgreSQL starts a fresh
repeatable-read transaction and locks the complete canonical source-table set
before its first snapshot query. The fixed set includes DispatchStops,
Dispatches, Drivers, ExecutionLegs, ExecutionVisits, LoadExecutionLegs,
SwitchParticipants, Trailers and Trucks, including navigation joins.

SHARE ROW EXCLUSIVE protects concurrent changes and prevents competing lock
upgrades. NOWAIT makes an already-active source writer defer publication instead
of waiting with a partial lock set. A conflict returns an expected planning
error; transaction disposal releases acquired locks. Ordinary reads can proceed.
These choices follow PostgreSQL's [explicit locking rules][locking] and
[snapshot/lock ordering requirements][lock-order].

This is intentionally a conservative compatibility boundary. Publications for
different trucks serialize, and source writes can wait for an active publication
to finish. There is no claim of improved throughput. Current assignments can be
resolved through legacy truck numbers, stop associations and native links; a
lock on captured rows alone would miss newly inserted or reassigned work.
Narrowing this boundary requires complete per-truck revision ownership across
every source writer. That remains work for the normalized core, rather than an
unverified assumption in the current model.

SQLite uses serializable isolation for isolated fixtures. Unsupported providers
and caller-owned transactions are rejected. The publication operation is not
automatically replayed after a failed write; a new operation must obtain fresh
inputs and use the normal result concurrency checks.

The PostgreSQL protocol has not been executed against a suitable isolated
PostgreSQL fixture. Lock contention, privileges, busy-source retry frequency and
production latency must be verified before release. SQLite tests do not prove
PostgreSQL behavior.

## Verification

Added 19 server cases covering:

- Validation and writes share one transaction with owned cleanup.
- Changed work immediately before entry prevents result publication.
- Uncommitted disposal rolls back multiple already-written result parts.
- Existing transactions, pre-entry cancellation and unsupported providers.
- Two independent SQLite connections: existing-row updates and new queue
  membership are blocked during publication and succeed after disposal.
- Final-boundary changes preserve base/live routes and progress results.
- Automatic and manual fuel publication preserve both previous saved copies.
- Route-choice publication preserves the previous choice and durable draft.
- ETA rejects changed work and removes an unpublished memory entry.
- A failure at ETA commit cannot report an uncommitted result as successful.
- Architecture checks cover the canonical reader's source tables and navigation
  joins, lock mode, cleanup, and validation ordering inside the transaction.

Initial integration exposed an obsolete direct database dependency in the fuel
service, SQLite's wrapped insert exception, and navigation joins absent from the
first source-inventory check. The dependency was removed and the tests were
corrected to verify the actual exception and complete source closure. No
architecture exception, skipped test or relaxed concurrency assertion was added.

Affected `bash test.sh routing dispatch fuel synchronization` passed:

- Server.Tests: 2,084 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed.

Required full `bash test.sh all` passed:

- Server.Tests: 2,307 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,880; no failures or skipped tests.

The pinned CSharpier check, scoped whitespace and local documentation links
passed. Logs and the 1,952-file pre-edit baseline are pinned under
`artifacts/managed/diagnostic-m81g8V`. The final file audit is pinned under
`artifacts/managed/diagnostic-WMUAC9`. This stage changes 34 source, test and
documentation files and retains the pre-existing work. No Client or migration
file changed.

Authenticated browser checks, live providers and production performance were
not exercised. No PostgreSQL server was started or installed for this work.

## Remaining scope

The guarantee covers facts represented in TruckItinerarySnapshot, including
queue membership, assignments, source review, transfers and visit facts. It does
not turn every calculation input into one transaction. Profile/settings caches,
external telemetry/HOS, prices, historical road evidence and standalone
base-road preparation retain their existing policies. A source change after a
successful commit still invalidates that result through the normal read checks.

No historical financial evidence or exactly-once financial command contract is
introduced. Next: consolidate the remaining historical/profile/compatibility
paths, establish narrow revision ownership and measure the publication protocol
before expanding toward accounting and driver settlement.

[locking]: https://www.postgresql.org/docs/current/explicit-locking.html
[lock-order]: https://www.postgresql.org/docs/current/sql-lock.html
