# Torque invoice status and historical completion

The user clarified that Torque `sent` means an invoice was sent to the broker
for completed work. The adapter previously copied that billing status into
the load's lifecycle. Historical loads without delivery actuals consequently
appeared as overdue work requiring review.

## Implementation and ownership

TorqueDispatchProvider now maps `sent`, case-insensitively with surrounding
whitespace ignored, to `completed`. Other statuses remain unchanged. This
provider vocabulary stays in Infrastructure. No financial formula, canonical
completion predicate, DTO contract or persistence schema changes.

Both ordinary synchronization and explicit ranged history imports share this
adapter. Existing SyncDispatchesCommand remains the sole import writer,
preserving source identities, stop identities, local overrides, transaction
boundaries and existing after-commit invalidation. There is no new cache or
per-card query. Performance was not separately benchmarked.

## Authorized data repair

A fresh backup was taken before changes:

- Path: `local-backups/torque-invoice-reconciliation-2026-09-27.dump`
- Bytes: 30,959,995
- SHA-256: `b09ebc20fe98458c1fba1779fee59ae0c6ca285e23df395f2a9c9b33d9a07a52`
- `pg_restore --list` succeeded; restore was not exercised.

The AMF-only preview compared all 417 identities returned for source order
windows January 1 through September 26. It found 325 stored `sent` records
whose current source status maps to completion. Repair used the normal import
command, not direct SQL or invented actual timestamps.

The first operation repaired 312 records with old delivery dates. A second
bounded reconciliation repaired one further record from a source order window
outside the configured refresh lookback. Already-correct records were skipped.
Twelve recent records remain deliberately deferred until adapter publication:
the old deployed worker could otherwise restore their old status. Publish the
adapter, then reconcile the remaining source-backed records through this tool.
No server release was performed as part of this correction.

Retired-truck records now comprise only completed and cancelled work:

| Truck | Completed | Cancelled |
| --- | ---: | ---: |
| 11 | 7 | 3 |
| 11001 | 68 | 7 |
| 11002 | 24 | 5 |
| 11003 | 32 | 4 |
| 11004 | 8 | 2 |

AMF1016, AMF1059, AMF1133 and AMF1172 all returned invoice-sent status from
Torque; their stored status is now completed. A fresh read through the real
Dispatch board owner, including planned/overdue work and with telemetry,
financial enrichment and ETA disabled, confirmed all four absent from the
current queue. Coverage was all nine board trucks. This is an application
read against repaired data, not a browser screenshot or synthetic production
test. Actual arrival/delivery timestamps were not fabricated.

## Verification and remaining scope

- Focused adapter/completed/import checks: 26 passed.
- Affected server Dispatch/Routing/Finance/Architecture: 1,656 passed.
- Matching Client categories: 714 passed.
- JavaScript type check, Dispatch 16 and Architecture 67 checks passed.
- Operational tool compiled with warnings treated as errors.

The first grouped run's Client build lacked local npm dependencies; server
checks passed. After installing locked dependencies, only the missing Client
and JavaScript checks were run. This is not a full-suite or deployment gate.

Regression coverage starts with an already-imported `sent` row, repairs its
status, checks identity and actual-date preservation, then replays with zero
changes. The existing import serialization/conflict policy is unchanged; no
new concurrency strategy is introduced. Runtime auditor coverage and bounded
source-backed detection are documented in the operational tool README.

Evidence is retained in `artifacts/managed/diagnostic-m2zhhl/evidence` in the
`codex/dispatch-history-import` worktree. The separate native Completed
Total/RPM read issue remains outstanding and is not fixed by status mapping.
