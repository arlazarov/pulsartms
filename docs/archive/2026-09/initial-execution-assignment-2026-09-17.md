# Initial ordinary execution assignment

Date: 2026-09-17. Local implementation; no deployment or working-database
change.

## Implemented boundary

Explicit ordinary assignment now enters accepted execution before a transfer
is planned. `InitialExecutionAssignment` creates the Trip, ExecutionLeg,
ordered ExecutionLegStop rows and load link in the owning serializable
transaction. The first immutable revision records the operator and correction
correlation; durable planning demand commits with it. Editor retries return
the original workspace receipt. Competing initial acceptance cannot create two
sequence-one load links or retain an orphan trip/history/planning request.

The load correction editor and complete truck-start confirmation share this
operation. Stops must resolve to one truck/crew/trailer set. Header driver and
trailer assignments remain available when visits do not repeat them. Unmatched
names, mixed resources, driver-only prefixes and invalid source chronology
stay under review without invented execution boundaries. The workspace derives
a pending-acceptance notice independently of mutable import notices.

Assignment alone is planned work. Explicit completion activates ordinary work;
final delivery closes it once its visits are complete. Unknown actual
timestamps remain null, including completion without a known time. Resource
conflicts are checked when work becomes active. Existing accepted execution
cannot be cleared by resetting the legacy truck override.

Legacy completion and operation commands now select accepted stops when
present, then use `ExecutionStopAcceptance` for revision locking, checks for
protected paths, history and planning demand. They reject stale identities and
transfer-boundary actions. The remaining source mirrors preserve compatibility
with source readers. They do not choose the accepted truck or overwrite
accepted history.

A transfer can follow the first ordinary assignment. Incoming planned work
requires confirmed receipt. Completion after receipt consults the
participant's confirmation without persisting a second confirmation into the
accepted stop. Cargo completion does not release an outgoing transfer.

## Verification

- `bash test.sh all`: 4,224 passing tests; 2,649 Server, 1,014 Client C# and
  561 JavaScript. No failures or skips. Ten new integration cases cover initial
  ownership, replay, rollback, resource ambiguity, source chronology, legacy
  writers and the complete assignment/transfer/delivery workflow. The existing
  provider-free creation test now verifies accepted history and planning demand.
- Strict CoreMigrationProbe build: zero warnings and errors. Pinned CSharpier
  checks passed for all 16 changed C# files.
- Isolated PostgreSQL probe: passed. Two independent serializable transactions
  observed the same unassigned load; only one committed its initial assignment.
  The loser retained no orphan trip, history or planning request. Persisted
  correction replay and the existing migration, identity, queue and accepted-stop
  checks also passed.

Retained evidence: [full suite][tests], [strict probe build][build] and
[PostgreSQL probe][postgresql]. The successful fixture
`pulsr_core_fixture_c1e5f059101c43ec87e1e04b84cb9803` was removed. PostgreSQL
ran on the existing server in a separately created database; no local database
server was installed or started.

[tests]: ../../../artifacts/managed/diagnostic-ZYvEUK/tests.log
[build]: ../../../artifacts/managed/diagnostic-ly0adw/build.log
[postgresql]: ../../../artifacts/managed/diagnostic-d1zKW6/postgresql.log

The first PostgreSQL probe run reached its cleanup and was correctly rejected
by the immutable-history trigger. The scenario was moved to the end of the
isolated fixture workflow. Its history remains until the fixture database is
dropped; no trigger or history protection was relaxed. The failed fixture was
removed.

## Remaining work

- Automatic import bootstrap still needs explicit readiness and resource
  proposal acceptance through the common owner. Source actuals on a planned
  assignment still require review; this slice does not silently activate
  work from polling.
- Driver-only prefixes and multiple resource intervals require explicit
  execution boundaries. Their legacy source representation remains until
  that workflow is implemented; this is not full ordinary-source migration.
- Transfer bootstrap and resource correction still have their own
  orchestration. Finish their writer unification before removing the
  compatibility Dispatch copies, source completion mirrors and planning
  overrides.
- Trip-wide lifecycle and historical resource intervals still need the wider
  custody/movement work. A recording timestamp is not an assignment start
  time.
- Finish source-road durability and publication revision boundaries, then run
  the release/cutover gate with protected identity and configuration
  preserved.

No schema change was needed for this slice. The existing pending migration
`20260917055902_RebuildExecutionStorage` remains unapplied to the working
database. No production performance claim, live-provider check or browser
verification is made by these server and database tests.

See the maintained [core plan](../../architecture/core-rebuild.md),
[architecture](../../ARCHITECTURE.md) and
[import guide](../../features/dispatch-import.md).
