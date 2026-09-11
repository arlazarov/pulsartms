# Explicit roles and fuel lifecycle hardening

## Scope

The approved work covered explicit administrator roles and reliability of Fleet Map
and fuel editing. It did not include deployment, a fuel-explanation panel, provider
calls, local database servers, or changes to fuel financial formulas.

## Administrator transition

Missing roles now resolve to Dispatch. Only one explicit Admin claim on an active
profile grants administration, including when a token still contains an Admin role.
The new integration regression checks missing, granted, revoked and inactive states
against the real role reader and authorization handler using SQLite.

The two user-approved active identities were assigned Admin on the pinned database
`neondb` at `ep-blue-cherry-aefp3o1q-pooler.c-2.us-east-2.aws.neon.tech:5432`,
resolved privately from local Development User Secrets with SSL Require.
Preflight found zero application-role claims and a valid unique role index.
`scripts/sql/assign-explicit-admins.sql` committed only the approved assignments;
post-commit verification at 2026-09-11 06:03:18 UTC found exactly two application-role
claims, both Admin on the approved active identities. No schema migration ran.
This verifies the configured database, not the Cloud Run connection configuration.

The affected-table custom-format backup is outside the repository:
`/Users/antonarlazarov/Developer/amftms-role-backup.wdDYhl/application-claims-before-explicit-admins.dump`.
The directory is mode 0700, the archive is mode 0600 and 3,845 bytes.
`pg_restore --list` verified that it contains the claims table data entry.
Backup SHA-256: `23dfc3488f077c96c34967b27b00ccc0d776cad65c78e38260fd1b322270b358`.
Executed SQL SHA-256: `2f698b5bc21b6a1e5ecfb78a9c8f2f9024b689b1335451f151eea0d3d5cd5430`.
No restore was performed; this table backup is not a full-database recovery drill.
The new application policy still requires a separately authorized deployment.

## Fuel request ownership

Four new component cases first reproduced a reuse race in Calculate Fuel:
the request used mutable component parameters after an awaited callback and accepted
late or mismatched results. The control now captures its dispatch and request owner,
cancels on dispatch change/disposal and suppresses stale completion callbacks.
Tests cover A→B→A with success and failure, switching during the busy callback,
and a response for another dispatch. Cancelling UI ownership does not undo an
already-started server calculation.
Fleet Map already keys this control by dispatch, which normally remounts it on a
switch. The new guards make the shared control safe without relying on that host
behavior; these tests do not establish that the keyed page previously displayed
another truck's calculation.

Two new cache cases reproduced missing cold truck-key publication and the
cross-truck invalidation caused by a global recalculation generation. Recalculation
now supersedes only its truck/dispatch keys and matching in-flight refresh owners.
Other trucks' previews and refreshes remain eligible. Existing entry, geometry
and expiration limits are unchanged. No new cache, polling loop or DI service was added.

## Verification

- Affected groups: `bash test.sh fuel fleet identity`, passed including architecture.
- Full suite: `bash test.sh all`, 1,430 server + 641 Client C# + 419 JavaScript tests passed.
- `npm run js:check --prefix Client`, passed.
- Isolated strict Release Client build: zero warnings/errors; publish and artifact
  integrity verification passed (252 assets, seven JavaScript dependency graphs).
- Staged Fuel Plan editor: eight desktop/mobile and light/dark scenarios passed,
  including 320px and 390px touch interaction; no browser errors or unexpected requests.
- Staged UI smoke: 44 page/theme/text-size cases passed.
- HOS/ETA browser probe: all ten Dispatch/Fleet viewport/theme scenarios passed,
  with no failures, browser errors or unexpected requests.

Local retained evidence:
`artifacts/managed/diagnostic-DxXo79/tests.log`,
`artifacts/managed/diagnostic-7uzb6O/tests.log`,
`artifacts/managed/scratch-IkE30b/build.log`,
`artifacts/managed/browser-fuel-editor-A1Cqnm/report.json`,
`artifacts/managed/browser-ui-pAXAY2/report.json`, and
`artifacts/managed/browser-hours-forecast-5tiSeC/report.json`.
Managed retention may eventually remove these local outputs.

The HOS/ETA browser probe initially stopped because it still expected 24-hour
`19:10` after the move to `07:10 PM`. Its expectations also predated the coordinated
popup inset, radius and shadow, and its provider stub omitted the production
viewport observer that sets the side-clearance property. The probe now loads that
actual observer and checks the existing UI contract, including exact shared shadow
comparison. No production styles were changed to satisfy the probe.

No isolated PostgreSQL fixture or integration test accounts were available.
PostgreSQL concurrency tests, full restore, live authentication/provider checks,
production performance and GPU lifecycle were not validated by this work.
The local development servers were not restarted and no code was deployed.
