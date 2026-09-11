# Independent integration settings — September 10, 2026

## Implemented scope

The user approved separate Settings connections for TorqueAI, Samsara and Google
for Emails, while retaining current credentials. Maps, Places and TomTom were not
moved. Existing credentials were not displayed, migrated, replaced or revoked.
Configuration files, Google consent and live mailboxes were left unchanged.

Application now owns the allowlist, presence-only status, Admin checks, blank
preservation, full Gmail tuple selection and optimistic concurrency. Existing
configuration remains the runtime source until a deliberate replacement is saved.
Google Client ID changes require Client secret and Refresh token together; token
or secret rotation otherwise retains the effective tuple. Explicit restoration
uses the retained complete deployment configuration and advances revision even
before a first saved override, preventing stale initial writes.

Infrastructure stores provider-bound Data Protection ciphertext in one versioned
row per connection, with separate contexts for concurrent operations. Malformed or
undecryptable bundles fail closed. The existing durable key ring is preserved;
its same-database, unencrypted-key-XML limitation remains and is documented in the
[feature guide](../../features/integration-settings.md).

Samsara and Torque resolve credentials per outbound request and never mutate
shared Authorization headers. Samsara also rejects untrusted destinations before
resolving credentials. Gmail captures a complete readonly OAuth tuple per client.
No provider verification, OAuth consent, watch registration or mailbox write is
triggered by opening or saving the new Settings forms.

Client has three independent compact cards with blank password replacement fields,
separate revisions and busy states, explicit restoration confirmation, safe errors
and draft clearing on success/cancel/disposal. Existing load-numbering and fuel
settings remain separate. Status means configured, not provider-verified.

## Verification

- Focused Identity/dependent architecture checks passed during iteration.
- Complete `verify-release.sh` with offline UI enabled passed: 1,381 Server tests,
  608 Client C# tests and 378 JavaScript tests; 2,367 total, zero failed/skipped.
- Strict Release solution/Client build had zero warnings and errors.
- The gate verified 249 assets and seven generated JavaScript dependency graphs
  in `artifacts/release.xYidW7/publish/wwwroot`.
- All 44 offline page scenarios passed. Settings additionally checks the exact
  provider list, empty secret readback, OAuth field shape, cancel/reopen clearing,
  shared control heights and editable-card overflow. Fixture network traffic and
  business writes remain blocked. Browser errors and unexpected requests were zero.
- Eight integration editor screenshots were inspected: 1440px/390px, light/dark,
  100%/200% root font size. New cards, inputs and actions were readable and not
  clipped. This is not an authenticated production acceptance or browser zoom test.
- Review identified and corrected the Application first-restore revision bypass,
  with an additional regression. Security review otherwise found no blocking issue
  in the new authorization, response/audit, bundle and revision handling.
- Isolated SQLite tests cover encrypted round trips across service-provider
  replacement, concurrent first writes/updates, tombstones and ciphertext tampering.
  These do not prove real PostgreSQL execution behavior.

Known unrelated visual finding: the existing fixed-width sidebar overflows into
page content at 1440px with 200% root font size. It was observed in full-page
Settings screenshots and was not changed by this credentials implementation.
Automated UI success is not a claim that all existing visual issues are resolved.

## Migration and activation — initial handoff

Migration `20260910175804_AddIntegrationCredentialSettings` is generated but
**unapplied**. Offline model consistency checks found no pending model changes.
The generated SQL adds only the new table and migration history entry; it contains
no credential data, backfill, update or deletion. No database connection was made
for migration generation or SQL inspection.

The new API and Client have **not been deployed or activated locally**. The running
local API/client and current production revision remain unchanged by this work.
Firebase reauthentication was still the recorded blocker from the prior deployment
task; no further deployment was attempted. Activate the additive schema and new API
before publishing this Client. An older API ignores the new overrides, so rollback
must account for its resumption of the retained deployment credentials.

No isolated PostgreSQL fixture was available; real PostgreSQL checks were not run.
No live provider/OAuth acceptance or production performance measurement was run.
Protected rows do not replace complete key-ring backups or independently protected
key storage. Corrupt saved credentials require operator recovery.

Ignored evidence: `artifacts/integration-settings-release-gate.log`,
`artifacts/integration-settings-identity.log`,
`artifacts/integration-credentials-migration/add-integration-credentials.sql`, and
`Client/test-results/integration-settings-release/report.json` with screenshots.

## Authorized database migration and local activation — 18:52 UTC

After the user explicitly requested migration, a read-only preflight resolved the
Development database to Neon `neondb` on
`ep-blue-cherry-aefp3o1q-pooler.c-2.us-east-2.aws.neon.tech:5432`, with SSL required.
Its last applied migration was `20260909020907_StoreTruckFuelPlans`; the only pending
migration was `20260910175804_AddIntegrationCredentialSettings`. This is the
application database, not a disposable test fixture.

The reviewed migration was applied through EF's migrator, restricted to that exact
target and inspected database identity. Post-migration read-only verification
confirmed the four expected columns, zero pending migrations and zero credential
override rows. No existing credentials, configuration, key-ring rows or business
records were changed by the migration. No backfill or rollback was performed.

The API was strictly published to the isolated
`artifacts/integration-settings-activation.NxuVis/api-publish` directory and started
on localhost:5086. Startup migrations, Gmail background maintenance and fleet
synchronization remain disabled for this local process. The Client on localhost:5067
now serves the previously verified immutable `artifacts/release.xYidW7/publish`
artifact, rather than mutable Client build output. The temporary 5068 preview
process was stopped after verification. Production application deployment was not
performed during this activation.

API liveness and the Client Settings route returned HTTP 200; unauthenticated
integration-settings access returned HTTP 401. The browser successfully booted and
showed the login page. Authenticated Settings acceptance requires the user to sign
in and was not completed. No credentials were entered or saved. Provider/OAuth
acceptance was not run. The full suite and offline UI results above apply to this
unchanged application code; this operational step did not rerun that full gate.
The live schema check is migration verification, not a PostgreSQL integration-test
fixture or a performance claim. Ignored activation evidence is retained under
`artifacts/integration-settings-activation.NxuVis`.

## Local login correction — 19:12 UTC

The initial published-artifact local launch above was insufficient for login.
Its embedded WASM configuration omitted `applicationEnvironment`, so the Client
selected Production and submitted login to port 5067. DevServer returned SPA HTML
instead of an API response. API liveness and correct CORS preflight on port 5086
had not established end-to-end login correctness.

Replaced only the Client process with a normal isolated Debug build under
`artifacts/localhost-client-recovery.W6kOAo/build`, launched from its real Client DLL
and generated manifests. Strict build passed with zero warnings/errors; the served
boot configuration now explicitly declares Development and selects port 5086.
The browser was reloaded. API, schema, credentials, CORS policy and application
source were unchanged by this correction. After reloading, the browser rendered
the authenticated Fleet Map and its map layer. The agent did not read or enter
passwords or submit login/credential forms. Integration-settings edits and live
provider acceptance were not tested, and the full suite was not rerun for this
operational launch correction.

## Production release — completed at 19:31 UTC

The user authorized publishing the server and Client. Firebase reauthentication
was completed before publication. The first full gate exposed an existing test
race: dispatch selection rendered route metadata before its asynchronous
`setRouteBytes` call. The component test now waits for a bounded explicit fake
interop signal before interacting; all original assertions remain. No production
logic was changed for this test correction. The targeted test passed 20 repeated
runs, its 55-test class passed five repeated runs, and `bash test.sh fleet` passed
264 Server, 188 Client and 293 JavaScript tests, including architecture checks.

The subsequent complete local gate and the official Client deployment gate passed
1,381 Server, 608 Client C# and 378 JavaScript tests: 2,367 total, none failed or
skipped. Strict builds and all 44 offline UI cases passed. Each gate checked 249
published assets and seven generated JavaScript dependency graphs. The deployed
Client is the exact newly gated `artifacts/release.2vVZFk/publish/wwwroot` artifact.

Server publication used `deploy-server.sh` without an environment override:

- Successful Cloud Build: `0f2581c4-1609-4852-8ea3-8f8f10cf8eeb`.
- Cloud Build independently passed the same 2,367 tests, strict builds and static
  artifact checks before producing the image.
- Image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:e59b8a3d7d2eae4968f7d7c72f92c2d96038d74c2d9fec2e29bc6e7242d42c73`.
- Ready revision: `amftms-api-00092-hrl`, receiving 100% of traffic.
- Ready transitioned at `2026-09-10T19:29:16.001169Z`; all readiness conditions were
  true. Existing tagged revision routes were retained.
- Previous revision `amftms-api-00091-rgh` remains the rollback reference. An older
  API ignores saved integration overrides, so rollback still requires coordination.

The Cloud Run connection endpoint matched the already migrated Neon database by a
metadata-only endpoint fingerprint. No second migration or credential backfill was
needed. Before/after configuration fingerprints matched, as did the database target
and service account; deployment credentials, API keys and OAuth values were not
printed or replaced. No key-ring data or integration override was manually changed.

Client publication used `deploy-client.sh` after the new API became ready:

- Firebase Hosting version: `883fb7260a4b8e63`.
- Live release: `1789068714780000`, released at `2026-09-10T19:31:54.780Z`.
- `/api/**` still routes to `amftms-api` in `us-east4` before SPA fallback.
- Both `https://tms.amfcarrier.com` and `https://amftms.web.app` passed exact SHA-256
  checks for all 77 manifest JavaScript/CSS/WASM assets, index, SPA fallback and
  API liveness. Direct API liveness passed too: 161 checks, zero failures.
- The production browser successfully booted the published login page. No account
  password was read, entered or submitted by the agent.

Bounded ERROR-level checks for the new revision returned no entries. This is not a
production soak or a performance measurement. Authenticated production Settings
acceptance and live provider/OAuth acceptance were not run. No isolated PostgreSQL
fixture was available; live schema verification above is operational evidence, not
a PostgreSQL integration-test suite. The existing sidebar 200% font-size limitation
noted earlier remains outside this release correction.

Ignored evidence: `artifacts/integration-settings-deploy-server.log`,
`artifacts/integration-settings-cloud-build.log`,
`artifacts/integration-settings-deploy-client.log`,
`artifacts/integration-settings-public-verification.json`, sanitized deployment
snapshots and error-count results, plus
`Client/test-results/integration-settings-hosting-ui/report.json` and screenshots.
