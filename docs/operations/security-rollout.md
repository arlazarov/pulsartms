# Security rollout checks

This document does not authorize new deployment, live provider calls or database
operations. The explicitly authorized database operation is recorded below.
Complete the normal release gate in
`docs/operations/release.md` as well as the checks below.

## Application role uniqueness

`20260908034738_EnforceUniqueApplicationRoles` was applied with explicit approval
on 2026-09-08; verification completed at 04:07:42 UTC. It adds a unique filtered index for `amftms:role`
per identity user and retains the general, nonunique identity-claim index. Other
claim types may still have multiple values. Its preflight deliberately aborts if
duplicate application roles exist; it does not choose a role or delete data.

The pinned target was database `neondb` at
`ep-blue-cherry-aefp3o1q-pooler.c-2.us-east-2.aws.neon.tech:5432`, resolved from local
Development User Secrets `amftms-api-local`, using SSL Require. This does not verify
that Cloud Run uses the same configuration. Read-only preflight found 20 applied
migrations, no existing role index, zero claim rows, and zero duplicate-role groups.
Only the reviewed 20-to-21 SQL ran, in one transaction with session-local
`lock_timeout = '5s'`, `statement_timeout = '60s'`, and `ON_ERROR_STOP`.
Post-commit read-only checks found 21 applied migrations and a valid, unique
`UX_AspNetUserClaims_ApplicationRole` index on UserId with the exact `amftms:role`
predicate. The existing primary and nonunique UserId indexes remain valid.
Claim rows, application-role rows, and duplicate groups remained zero. No identity
or role assignments were changed; no other migration, deployment, Gmail registration,
or provider operation ran.

The pre-change full custom-format database backup is stored outside the repository:
`/Users/antonarlazarov/Developer/amftms-db-backup.YEV95x/neondb-before-20260908034738-verified.dump`.
Its directory is mode 0700; the archive is 0600, 108,674,407 bytes, SHA-256
`16f106b572e9a11ecb5c01776fe59f8c680b2fc0a395ba834f4eaa4a8fd9a860`.
PostgreSQL 18.6 backup tooling matched the server. `pg_restore --list` succeeded
with 166 entries and the backup emitted no warnings; no full restore was tested.
The reviewed 603-byte SQL is beside it as `20-to-21-EnforceUniqueApplicationRoles.sql`,
SHA-256 `31ee123ff81c21e8aba7eaf60beae7209c77ba9b2beeb073a6277f7597dbe471`.
Execution added only the two approved transaction-local timeouts. Preserve these
artifacts; the similarly named dump without the `-verified` suffix is a failed,
zero-byte initial attempt, not a backup.

Before applying it, confirm the exact database and a restorable backup, inspect
the generated migration SQL, and count duplicate application-role groups using a
read-only query. Review each affected identity with the authorized administrator
and choose its intended role explicitly. The application's explicit role setter
repairs duplicate application-role claims during an authorized assignment;
ambiguous claims resolve to Dispatch until repaired. Accounts with no explicit
role retain the existing legacy Admin behavior.

Do not proceed with automatic startup migrations while duplicate groups remain:
a failed migration prevents the new revision from starting. Use a reviewed,
staged migration window when needed. The index is additive and compatible with
older readers, but older concurrent writers can encounter its uniqueness guard.
Coordinate revision rollout accordingly. Reverting this migration removes only
the added index; it does not restore any separately reviewed role edits. Keep
their audit records and use the reviewed backup/recovery procedure if required.

SQLite tests cover first-assignment concurrency, claim uniqueness, duplicate
reads and repair. They do not prove PostgreSQL row-lock behavior or production
migration duration; validate those against a disposable PostgreSQL instance before
release. The live checks above verify migration metadata and index validity on an
empty claim table, not PostgreSQL write concurrency under load, end-to-end
authorization, restore readiness, or production performance. The automated test
suite itself makes no live database validation claim.

## Provider attempt accounting

TomTom reserves an existing `RoutingApiCalls` ledger row and commits quota usage
before HTTP dispatch. Malformed responses and cancellation cannot roll back an
attempt that may have been billed. An interrupted attempt keeps a one-minute
retry delay; invalid response shapes have a five-minute delay. This deliberately
counts a reservation even if the process exits just before dispatch. Configured
daily and per-minute limits are unchanged. Tests use fake HTTP and local SQLite,
not a paid provider or production database.

## Browser session storage

Browser credentials now use one `auth_session` JSON envelope with a login identity
and both tokens. A new login creates a new identity; token refresh preserves it.
The shared storage module runs reads, legacy upgrades and compare-and-set writes
under one origin-wide Web Lock. This prevents a stale tab from overwriting or
clearing another login. Logout is also bound to the captured login before HTTP
dispatch, and clearing follows that identity through same-login token refresh.

Each tab pins the login identity it first adopts, including an initially anonymous
state. A different login or logout in another tab makes the old tab anonymous and
blocks new API requests before dispatch. It does not silently transfer an old
form to the other account. Refreshing tokens for the same login still works across
tabs. Reloading the page or explicitly signing in within the tab adopts a new
login; there is no automatic reload loop. Detection occurs when session state is
read or an API request is attempted, so an idle tab may retain its old display
until then, but cannot send that display's next operation as another account.

The previous `access_token` and `refresh_token` pair migrates automatically only
when no envelope exists. An invalid or cleared envelope never resurrects those
legacy values. Reload older open tabs when rolling out the envelope format; code
from older revisions does not participate in the new storage protocol. Current
browsers over HTTPS or localhost are required. Missing Web Locks fails closed,
with a sign-in message; there is no unlocked storage fallback. Node tests simulate
cross-tab lock scheduling, while C# tests cover session-bound HTTP and interop.
These checks alone are not a real multi-tab browser validation.

## Secret-free artifacts

API project content excludes local Gmail credentials/token directories, local
settings, secrets and environment files. A publish target also rejects those
filenames in the output, including leftovers from an older publish. Publish to a
new empty staging directory and retain the normal externally supplied production
configuration mechanism. Existing source credentials are not deleted or rotated
by these safeguards, and older generated artifacts must be handled separately.
Do not print credential contents when inspecting filenames or artifact manifests.

## Logging

Successful fast request polling records aggregate metrics without per-poll
Information logs. Slow completed operations retain timing diagnostics. Expected
cancellation and unexpected failures do not add a second timing log; the HTTP or
background boundary remains responsible for the failure event. Password-reset
results no longer write ad-hoc console messages. Tests verify representative
routing behavior; production log volume has not been measured.
