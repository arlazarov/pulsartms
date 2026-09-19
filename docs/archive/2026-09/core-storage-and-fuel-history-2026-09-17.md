# Native stop storage and saved fuel history

Status: implemented and verified locally on September 17, 2026. The working
database and deployed API remain unchanged. This is part of the core rebuild,
not completion of its remaining ownership and migration work.

## Changes

- ExecutionLegStops replaces mutable native StopsJson storage. Accepted order,
  occurrence identities, appointments, actuals and correction evidence survive
  tracked replacements. Resource assignment remains owned by the leg.
- Shared row projection serves native route, mileage, workspace, reconciliation
  and transfer operations. Immutable cancellation receipts retain their format.
- Equal decimal coordinates produce the same route identity regardless of
  trailing-zero representation. Older affected saved hashes may recalculate.
- Fuel persists historical lookup seeds and accepted signatures. Shared saved
  input validation checks these and remaining saved roads in one fresh snapshot.
  Already traversed connections no longer invalidate the remaining plan.
- The migration validates old rows before replacing their storage. Downgrade
  reconstructs current accepted facts, including edits made after upgrade.

## Verification

- Full suite: 4,121 passed, comprising 2,548 Server, 1,012 Client and 561
  JavaScript tests. Evidence: diagnostic-nGR4nO/full.log.
- Release gate: strict build without warnings, the full suite, 52 offline UI
  checks, 273 published assets and seven JavaScript dependency graphs passed.
  Evidence: diagnostic-4kvLBZ/release.log.
- Separate PostgreSQL fixture: upgrade, typed edits, downgrade, re-upgrade and
  twelve invalid-source rollback cases passed. No database server or container
  was created. Each uniquely named fixture database was removed afterward.
- A complete working-database backup restored successfully into a separate
  fixture on the existing server. Migration retained all four native legs,
  eight native stops and the existing business table counts. Evidence:
  diagnostic-YDCRP6/result.json. The application database was not a test fixture.
- The backup contains 230,827,821 bytes and 388 catalog entries. SHA-256:
  `645c2e6c6f4316724b5c550ba043eda48c6fe8ce4313ac7c0548f80ba3d72a23`.
  Recovery files remain in a private directory outside disposable artifacts.
- Stronger source-to-storage assertions subsequently passed in SQLite and the
  synthetic PostgreSQL fixture: diagnostic-A96paq. That strengthened comparison
  has not yet repeated the complete backup restore rehearsal.

Evidence paths above are under artifacts/managed and retained with .keep.
Authenticated live browser checks, production contention and production
performance measurements were not run. Passing offline UI checks does not prove
complete visual correctness.

## Rollout and remaining work

Migration 20260917045303_NormalizeExecutionLegStops is not applied to the
working database. The old API cannot read the replacement schema. Cutover must
stop old readers/writers and use a compatible API before restoring service.
Restoring Google Cloud authentication does not authorize deployment.

Remaining core work includes unified ordinary/native ownership and versioned
history, remaining compatibility consumer removal, complete cross-process
revision ownership and narrower publication locking. Financial agreements and
actual fuel/toll imports remain later product work.
