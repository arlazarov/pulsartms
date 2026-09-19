# Accepted execution history and reset decision

Local implementation and verification on September 17, 2026. Nothing in this
report was deployed or applied to the working database.

## Accepted version retention

ExecutionLegRevisions records native leg resources, ordered stops, load links
and transfer confirmations in the owning mutation transaction. Explicit source
acceptance, source synchronization, workspace edits, resource/stop corrections,
and transfer planning/cancellation/release/receipt use the shared recorder.
Retries retain their recorded result without inserting another revision.

History documents are immutable. Application persistence rejects edits/removal;
PostgreSQL also protects them with a trigger. Unknown actual times stay unknown
even when a transfer is confirmed. Source observations and route-choice versions
remain outside accepted execution revision history.

The prepared migration can capture an explicitly marked observed baseline of
existing native legs. It does not invent earlier revisions or change actors.
Its downgrade refuses to discard new accepted history. This preservation path
was prepared before the user's clean-reset decision below.

Mileage no longer reorders accepted native stops by source Sequence. Both saved
road and odometer paths retain accepted Position order. Regression cases use
source sequence numbers in reverse order and verify correct attribution.

## Verification

- Full suite: 4,130 passed, consisting of 2,557 Server, 1,012 Client and 561
  JavaScript checks, with no failures/skips. Evidence:
  artifacts/managed/diagnostic-vV46E3/full.log.
- PostgreSQL synthetic fixture: stop upgrade/write/downgrade/re-upgrade,
  twelve rejected-source rollback cases, history baseline comparison, accepted
  append, database immutability and guarded downgrade all passed. The fixture
  database was removed. Evidence: diagnostic-V19Ug1/postgresql.log.
- The full working-backup rehearsal for the preceding stop-storage slice
  remains the earlier recorded result. A new restore for both migrations was
  deliberately interrupted after the reset decision; its fixture was removed.
  It is not a passed migration check. Evidence: diagnostic-4nwvm4/result.json.
- Read-only working-data inventory found 19 non-native loads with status sent
  carrying different truck identities across source stops. This demonstrated
  why a preserving backfill could not assign one truck to each historical load.
  Evidence: diagnostic-zK8Kho/assignment-inventory.json.

Evidence directories are under artifacts/managed and pinned with .keep. No
local SQL server or database container was started. Production contention,
production performance and authenticated live browser behavior were not tested.
The earlier release-gate pass predates this history slice; it is not a release
verification claim for the new code.

## User decision and remaining scope

The user clarified that existing data need not be retained except users.
The core specification now prioritizes a clean operational transition, retaining
the full profile/identity/role boundary and Data Protection keys. Existing
business-data backfill and compatibility solely for old rows are no longer
completion requirements. Integration configuration is not reset unnecessarily.

History remains necessary for newly accepted work and later financial evidence.
Unified ordinary/native ownership, common visit identities, remaining planning
consumer removal and cross-process revisions remain implementation work. The
clean reset must be prepared and verified against an isolated fixture before
the compatible release and working-database cutover.
