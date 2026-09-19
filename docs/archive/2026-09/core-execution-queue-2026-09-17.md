# Execution queue ownership and recovery

Date: September 17, 2026. Local implementation only; no deployment or changes
to the working database. The user authorized an isolated local database if
needed. The existing PostgreSQL fixture workflow was sufficient; no local
database server was installed or started.

The [core specification](../../architecture/core-rebuild.md) remains the
authoritative unfinished plan. This change covers native execution outbox
claims and acknowledgements, not all background scheduling.

## Corrected behavior

The former store selected a due request and then claimed it with a separate
update. That update did not recheck the retry deadline. Completion checked the
lease token but accepted expired leases, and an unclaimed request with a null
token could acknowledge an unleased row.

PostgreSQL now selects and claims a due row in one statement, skipping rows
locked by other owners. The relational fallback rechecks the deadline in its
conditional update. Completion and retry require an unexpired, non-null lease
on an incomplete request, and return whether acknowledgement succeeded.

Requests retain independent identities and captured assignment revisions.
Finishing older work leaves a newer request pending. A restarted worker can
recover an expired claim with a new token; the former owner cannot change it.
Provider calls remain outside these claim transactions. Existing retry delays
and calculation/publication checks are unchanged.

## Verification

- Nine new SQLite integration cases passed, including an injected deadline
  change between candidate selection and the claim update.
- Full `bash test.sh all`: 2,572 Server tests, 1,012 Client C# tests and 561
  JavaScript tests passed: 4,145 total, with no failures or skips.
- CoreMigrationProbe built with warnings treated as errors: no warnings/errors.
- The isolated PostgreSQL fixture passed concurrent nonblocking claims, restart
  recovery, expired/former-owner rejection, durable retries and independent
  acknowledgements. It also repeated the clean migration, protected identity,
  accepted history and downgrade-guard checks successfully.
- Fixture `pulsr_core_fixture_7d3521bf61b249a1928cb996f1a5143b` was removed.

Pinned evidence: `artifacts/managed/diagnostic-fncfUo/tests.log` and
`artifacts/managed/diagnostic-2tfTMs/postgresql.log`.

No authenticated browser checks or new release gate were run for this server
ownership change. The earlier release gate describes the earlier working copy.
Production throughput and contention are unmeasured. The operational reset,
working-database migration, unified import owner, remaining calculation
adapters and replacement of memory queues are still pending.
