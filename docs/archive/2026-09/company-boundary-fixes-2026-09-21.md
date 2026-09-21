# Company boundary corrections — 2026-09-21

This implements the six confirmed findings from the
[refactor audit](refactor-audit-2026-09-21.md) against baseline `680f186`.
It is local implementation evidence, not a deployment record or certification
that every part of the product is ready for multiple independent carriers.

## Changes

- Fleet metadata now uses the existing company-aware ReadCache. Assignment
  changes invalidate its catalog generation. Direct and synchronized telemetry
  snapshots are keyed by company, including the retained latest response.
- Fleet synchronization retains distinct company cursors, vehicle snapshots,
  pending planning and job schedules. Nested jobs retain the selected owner
  instead of iterating the complete company roster again. The server lease
  stays global. Legacy checkpoint fields remain the original carrier's state.
- HOS clocks, refresh cooldowns, provider history and driver catalogs no longer
  share another company's values. A HOS pass selects its company before reading
  the provider and persists only that pass's readings. The bounded location
  stream and driver catalog discard retained data when changing companies.
- Fuel price/calendar signatures and retained fuel geometry are company-keyed.
  Raw saved-fuel upserts also require matching ownership on conflict.
- Integration bundles use a company/provider key and company-bound protection.
  Only the original carrier can read legacy protected bundles or use deployment
  credentials. Gmail watch checkpoints are separate. The configured, validated
  push mailbox explicitly selects its original owner before importing.
- Runtime tracked writes reject absent ownership, foreign rows and ownership
  changes, including deletes. Explicit tooling contexts without the company
  service retain bootstrap access; this does not make raw SQL tenant-safe.
- Expense recording checks truck, driver, trailer and execution-leg references.
  Attribution checks every target load. Unchanged amounts no longer suppress
  changes to basis, reason or manual-override meaning and their audit history.

## Regression coverage

New checks cover alternating companies with the same provider IDs and price
keys, separate snapshots and HOS cooldowns, separate integration revisions,
legacy credential reads, deployment credential restrictions, foreign and
missing expense references, metadata-only attribution edits and tracked writes
without the correct owner. Existing fixtures explicitly declare their company;
architectural assertions were not relaxed.

The credential migration was exercised on the existing isolated PostgreSQL
fixture. A pre-migration credential table retained its ciphertext and revision
under the original company, accepted another company's row for the same
provider and preserved filtered reads. This focused upgrade test is not a
rehearsal of the entire historical migration chain.

## Migration and rollout

`20260921174619_IsolateCarrierIntegrationCredentials` adds company ownership to
the credential table and changes its primary key. Existing rows are assigned
to the original carrier. The model snapshot and guarded reset-script schema
inventory were updated together; the reset script was not executed.

The application database has not been migrated. Drain old writers and apply
the migration before running the new credential API. New protected writes are
not readable by the old API. Automatic downgrade is deliberately refused;
rollback requires explicit recovery rather than merging company credentials.

No localhost process was started and nothing was deployed. Browser behavior,
live provider access, production performance and backup restoration were not
tested. Multi-mailbox push routing and the remaining SaaS rollout work are
outside this correction; do not infer full SaaS readiness from these fixes.

## Final verification

`bash test.sh all` passed: 3,091 server tests, 1,052 Client C# tests and
623 JavaScript tests, for 4,766 passing checks with no failures or skips.
The run includes architecture checks and the isolated PostgreSQL tests.
The Client was built as part of this run. `git diff --check` also passed.

The complete log is retained under
`artifacts/managed/diagnostic-ROUskR/check.log` with a `.keep` marker.
