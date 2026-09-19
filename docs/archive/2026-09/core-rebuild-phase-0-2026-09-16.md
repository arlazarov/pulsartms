# Core rebuild phase 0: model and acceptance baseline

Date: 2026-09-16. Scope: local design and source characterization.
Status: design package complete; operational baseline gates remain pending.
No implementation, database migration or deployment is claimed.

## Delivered

- [Core specification][spec]: vocabulary, data ownership, invariants and a
  bounded first implementation slice with an explicit old-path removal gate.
- [Acceptance catalog][cases]: 16 operational scenarios and five synthetic
  financial probes, with existing test homes and explicit coverage limitations.
- Local metadata baseline: 1,909 existing files hashed before these changes;
  HEAD was fd5dd2d078d8777227cc00bac4908454b7eff129. The working tree already
  contained 1,556 porcelain status entries. No reset, stash or commit was made.
  Hash metadata is comparison evidence, not a backup or restorable checkpoint.

## Decisions for the first slice

Keep the existing database and HTTP shape. Introduce an immutable canonical
truck itinerary read in Application/Execution using existing selection rules.
Move one planning read off GetDispatchBoardQuery before normalizing storage.
Do not combine this with changed formulas, multi-company rollout, worker host
extraction, payroll, native load creation or a UI redesign.

Existing GetExecutionItineraryHandler and ExecutionLoads.ReadAsync already
cover parts of native execution. Reuse and extract their rules rather than
create a competing implementation. Their current output exposes Domain entities;
the new consumer contract should make identity and revisions explicit without
using Dispatch as a substituted execution object.

The next implementation package should characterize C01/C02/C04/C14, define
the reader contract, compare results using identical fixtures and migrate one
read consumer. It must inventory remaining consumers and remove the selected
consumer's Board dependency. This does not complete the whole core rewrite.

## Plan corrections

- Full tenant migration is no longer a prerequisite for the first read slice.
  Explicit ownership remains a design constraint; isolation is not claimed.
- A generic visit record with arbitrary optional fields is not the target.
  Shared identity and operation-specific rules are separate concerns.
- Financial examples constrain operational evidence now. Financial tables and
  a formula engine are not created speculatively.
- Document lifecycle and worker enablement fixes remain separate stabilization
  changes so their behavior and checks can be reviewed independently.

## Verification

Read current project rules, documentation indexes, execution entities and
queries, the planning consumer and existing execution test examples.
Checked new document links, 80-column formatting and the scoped diff.
Compared all baseline file hashes to ensure only intended documentation changed.

No application tests were rerun for this documentation-only stage. The earlier
audit's 3,757 passing tests are historical evidence, not a fresh result for this
stage. No production database, provider or performance measurement was used.
No migration was authored or applied.

## Pending gates

- A restorable source checkpoint that includes existing uncommitted work; the
  metadata manifest alone cannot satisfy this gate.
- An authorized isolated PostgreSQL fixture and restore evidence before any
  persistence cutover. No SQL container or automatic server installation.
- Performance and freshness baselines on an appropriate permitted environment.
- Real compensation examples, contract-effective-date policy and approval roles
  before financial implementation. Synthetic examples do not decide policy.
- Accounting scope: external export or an internal general ledger, before
  implementing ledger-specific behavior.

These gaps do not prevent local characterization and read-contract work.
They prevent claiming migration, performance or financial readiness.

[spec]: ../../architecture/core-rebuild.md
[cases]: ../../architecture/core-rebuild-scenarios.md
