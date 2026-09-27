# TorqueAI dispatch history import

The user explicitly authorized loading the missing historical dispatches.
Company: AMF only. Source order-date range: January 1 through September 26,
2026, inclusive. Operation completed September 27 UTC after the authorized
API/Client publication of candidate `64c8ebfc` (record `3ff99739`).

## Result

Torque returned 417 distinct source identities. Of these, 45 were already
linked and were skipped; 372 missing dispatches were imported. A separate
read-only provider/database reconciliation after commit found all 417 linked
and zero missing. January and February windows returned no data. This proves
coverage of the API responses for the requested date range, not that Torque
contains no older records outside those responses.

Import windows added 66, 46, 41, 59, 72, 77 and 11 loads respectively from
March onward. Cancelled records and original source statuses were retained.
No completion facts were invented. This is not a claim that all records
qualify for Completed, have complete financial data or resolved resources.

## Ownership and safety

Tool commit `a74a6b1b` calls the existing Torque date adapter and feeds only
missing source identities into `SyncDispatchesCommand`. Its serializable
transaction, source-link identity, number allocation, stop matching and
execution acceptance remain authoritative. No direct SQL writes, migration,
operational reset, outbound message, payment or forced replan was performed.
The tool starts no host/background jobs. Production's normal background
reconciliation retains its existing behavior.

AMF company context was checked before every window. Each committed window
was verified against its source identities. Existing linked records were
not submitted to the import command. Cross-process display cache refresh
still follows normal freshness policies; immediate screen refresh was not
measured. Existing source identity/acceptance guards provide consistency
coverage; no new stateful business rule or auditor rule was introduced.

## Evidence

- Preview: `diagnostic-FiYCb1`, 417 source / 45 existing / 372 missing.
- Apply and post-import verification: `diagnostic-ofxkSR`, pinned counts-only
  logs, retained under this worktree's managed artifacts.
- Build `scratch-AVKXDJ`: zero warnings/errors; pinned CSharpier check passed.
- Range and backup-hash negative checks rejected before database/provider
  access. No production test writes or extra full test run was performed.
- Pre-import release backup:
  `local-backups/pulsartms-release-backup.AzEK1i/before-2026-09-26-night-release-64c8ebfc.dump`
  in the main checkout, independently hash-verified and checked by the tool:
  `b29c6ca2e9e3109561969f6b81e0baaaea6fdbc5dc767ee863e4e1e1167f6604`.
  Restore was not rehearsed.
- Root independently verified API build/digest, 100% traffic and old revision
  drain, plus live frontend hashes and health 200. Evidence is in the main
  checkout's `artifacts/managed/diagnostic-GkD1hu`.

The tool and this report are local operational source, not another deployment.
