# Dispatch provider independence — September 17, 2026

## Implemented locally

TorqueAI is an optional Infrastructure adapter selected by
`DispatchImport:Provider`. Empty configuration removes both provider
registration
and its synchronization loop. Existing fleet, telemetry and planning schedules
continue. The checked-in selection remains `torqueai`; this work does not turn
off the deployed integration or erase credentials.

PulsR now creates loads independently through an authorized Application command
and `/dispatch/new`. The native workflow includes validation, transactional
number
allocation, actor history, idempotent receipts, address lookup, assignment and
manual completion. Uncertain Client submissions preserve the exact payload/key.
All Dispatch views include planned/unassigned loads. The existing active-work
selector now recognizes `Delivery` alongside `Drop Off` for final completion.

Import identity is the persisted `(Provider, ExternalId)` mapping, not a display
number. Another provider, or a native load with the same number, cannot be taken
over by import. Restart replay and changed external display numbers retain the
mapped internal identity. Torque currently supplies its load number as the
source
key because the existing response contract exposes no separate source ID; this
assumption stays inside its adapter.

Native loads own their workspace fields/stops. Existing local-override and
accepted-execution reconciliation safeguards remain in place. An architecture
check rejects Torque names/types in operational Application and Domain code;
the integration credential catalog remains an explicit integration boundary.

## Schema

The two new tables, DispatchSourceLinks and DispatchNumberCounters, are included
in the single pending `20260917055902_RebuildExecutionStorage` migration.
Up requires the already approved explicit clean operational transition and now
also rejects existing loads before modifying schema. It does not invent mappings
for old data. Down additionally rejects new load/link/counter state before
drops.
Users, authentication, roles, profile preferences and protected integration
configuration retain the existing preservation contract.

## Verification

- `bash test.sh all`: **4,214 passed**: 2,639 Server, 1,014 Client C#,
  561 JavaScript; no failed/skipped tests.
- Strict Client Release build and local staged artifact completed with warnings
  treated as errors. Nothing was published to an external service.
- Compiled browser creation workflow passed at 1440px light, 390px dark,
  320px light and 390px dark with 200% root text. Two explicit synthetic address
  lookups populated stops; one explicit save navigated to the new load. Typing
  performed no writes. No browser errors, unexpected requests or horizontal
  document overflow were recorded. Desktop and mobile screenshots were
  inspected.
- Isolated PostgreSQL fixture verified native creation/retry, imported-number
  collision isolation, stable source identity, four concurrent creations with
  distinct numbers, and a downgrade guard retaining the new work.
- The same fixture passed the existing clean transition, immutable history,
  execution/planning queues, accepted-stop ownership and rollback checks.
  Protected identity/configuration digests, password and role remained valid.
- Pinned CSharpier formatting and `git diff --check` passed.

Evidence under `artifacts/managed/`:

| Evidence | Run |
| --- | --- |
| Final full suite | `diagnostic-JBzCtT/tests.log` |
| Strict migration probe build | `diagnostic-pDOpb6/build.log` |
| PostgreSQL checks | `diagnostic-n21siy/postgresql.log` |
| Final strict Client artifact | `diagnostic-m1en1a/build.log` |
| Final browser report/screenshots | `browser-ui-lzSKFW` |

The fixture `pulsr_core_fixture_8dc70e5083694dcd9343376d94914947` was created
on the existing PostgreSQL server and removed after verification. No local SQL
server or container was started; no application database was used as a fixture.

## Limits and remaining work

No working-database migration, reset, Cloud Run deployment or credential change
was performed. The full release gate and live provider/authentication checks
were
not run. Browser results use synthetic APIs; PostgreSQL results use an isolated
fixture. Production latency and contention remain unmeasured.

The broader core rebuild remains open: ordinary assignment bootstrap into the
accepted execution-leg owner, source/resource proposals, remaining mutation
unification and final cutover. This change makes the load workflow independent
of TorqueAI; it does not claim that every legacy core path has been removed.
Accounting, settlements and actual fuel/toll imports remain future modules.
