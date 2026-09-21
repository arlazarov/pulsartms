# Development and operations

Read [architecture](../ARCHITECTURE.md), [UI controls](../ui-controls.md), and the root AGENTS.md first.

## Build and run

### Local iteration and release boundaries

Develop and verify locally by default. Previous cutover approval does not cover
future releases. Publish a reviewed batch only after a new explicit user request;
do not run Cloud Build or deployment scripts for each edit.

Use this iteration loop:

1. Reproduce the affected invariant in the existing feature tests. For Dispatch,
   run `bash test.sh dispatch`; use the other groups in [test selection][tests]
   when appropriate. The runner reuses `artifacts/tests` and includes dependent
   categories and architecture checks.
2. Build the Client when changing Razor or Client C#. Reuse the running local
   application only with an isolated development database. A localhost address
   does not make a configured remote database safe for experimental writes.
   Check the effective database configuration before starting the API; do not
   reuse production credentials or enable migrations against the working DB.
3. Use existing offline browser fixtures for UI scenarios without live data.
   For a narrow Dispatch inspection, use `dispatchWorkspaceSmoke.mjs` with
   `DISPATCH_WORKSPACE_CASE=1440-light-100` and a current staged Client, following
   [the browser guide][browser]. Rebuild the stage after Client changes; an old
   stage cannot verify new source. Run the required matrix before release.
4. Run the full suite for shared contracts, persistence, dependency injection,
   authentication or test infrastructure changes, even during local iteration.
   Before an authorized release, run the complete release gate. Targeted checks
   are iteration feedback, not a replacement for release verification.

Do not start the full API against production to obtain a convenient local preview:
background workers can write even when the browser only reads. Until a separate
development database is configured, use isolated test fixtures and offline
browser scenarios. Follow the database restrictions in [test selection][tests].
These steps avoid per-edit cloud publication; no fixed speedup is claimed.

[tests]: ../testing.md
[browser]: ../../Client/tests/browser/README.md

### Explicit local takeover of the working database

A local takeover requires an explicit user request to stop the cloud API and
use its database locally. Preserve the previous Cloud Run scaling/traffic
configuration and local User Secrets privately before switching. Stop cloud
instances, including any tagged revisions, before enabling local workers.
Do not use the working database as an automated test fixture.

`BackgroundOperations:Enabled=false` disables all hosted application operations
through the shared Infrastructure worker. It defaults to true; it does not
disable HTTP commands or database startup migrations.

`BackgroundOperations:Roles` narrows which operations one instance runs, named
after the work rather than the interface: `DriverHosRefresh`, `PlanningRefresh`,
`BaseRoute`, `EtaRefresh`, `TruckHistory`, `OdometerCapture`,
`ExecutionPlanning`, `GmailWatch`, `FleetSynchronization`. Absent or empty means
all of them, so an instance that states no roles behaves as before. `Enabled`
still overrides it, and stating a role does not enable an operation that its own
feature switch has turned off. This is what lets an instance serve requests
while another runs the work, and what lets a read-oriented local instance still
refresh HOS, which is served from a snapshot a background operation fills. Keep
`Database:ApplyMigrations=false` and apply only reviewed pending migrations
explicitly during a cutover. The ordinary development database remains the
default for subsequent implementation and automated verification.

### Current workstation override

The local takeover ended with the authorized September 21 release. Cloud Run
`amftms-api` in `amftms/us-east4` serves the new revision with automatic scaling
and a one-instance revision limit. Local API and Client processes remain stopped.
Local API User Secrets still point to the working `neondb` database with
background operations and synchronization enabled. Automatic local migrations
remain disabled. Do not start that local API while cloud workers are running.

The isolated databases below remain available, but are not the current interactive
API target. Restore `before-local-takeover-secrets.json` from the private
workstation development directory before experimental writes or test data setup.
Do not resume Cloud Run workers while the local working-database API is running.
Original cloud configuration is saved privately as
`cloud-before-local-takeover.json`; it used automatic scaling, minimum one and
maximum twenty instances. The current release uses the narrower revision limit
in `deploy-server.sh`; do not restore the old settings as a routine startup step.

The stop-driver and Shipment/Border migrations dated 2026-09-18 were applied
additively in one transaction after cloud shutdown. No user data was reset.
Gmail push webhooks now reach the cloud API again. No public localhost tunnel
exists. See the [September 21 release record][company-release] for migration and
verification evidence.

[company-release]: ../archive/2026-09/company-isolation-release-2026-09-21.md

### Dedicated development and test databases

This workstation uses `pulsr_development` on the existing remote PostgreSQL
server, with a dedicated `pulsr_developer` login. The saved development
configuration selects that database; restore it before local iteration as
described above. This is database isolation,
not a PostgreSQL server running on localhost; network access is still required.
Current migrations are applied. No production rows or users were copied.

The development database also contains synthetic manual-testing data: three
`DEMO-` trucks, two trailers, three `Demo Driver` records and three loads.
`DEMO-CHANGE-DRIVER` starts with one assignment, `DEMO-YARD-HANDOFF` has
confirmed release/receipt and separate drivers before/after the yard, and
`DEMO-UNASSIGNED` supports testing initial assignment. These are fabricated
facilities and coordinates, not copies of production loads. Their IDs are in
`~/.local/share/pulsartms/development/demo-workspace.json`. They are editable;
do not reset them automatically on application startup. No live GPS or HOS
observations are supplied by this seed, so Fleet Map is not a live fleet demo.

The saved development configuration disables synchronization, Gmail background
maintenance, Dispatch imports and automatic migrations. Starting the API with
that configuration does not opt back
into those integrations. Review explicit provider calls separately when adding
development data. Restart an already running API to load the new settings.

A separate `pulsr_core_fixture_` database with a random suffix belongs to
`pulsr_test_runner`. Use it only for disposable synthetic checks, never for the
interactive development workspace. Both logins lack superuser, role creation
and database creation privileges and have no table privileges on production.

Connection records are stored only in the private workstation file
`~/.local/share/pulsartms/development/databases.json`; the previous local
configuration is backed up alongside it. These files contain secrets: do not
commit them, print them or copy them into disposable artifacts. The first record
is development; the second is the migration fixture. Supply the fixture's
connection in `PULSR_MIGRATION_TEST_CONNECTION` to the existing
[migration probe](../../tools/CoreMigrationProbe/README.md). It requires an empty
fixture and populates synthetic data. Clear only the explicitly identified
fixture between runs; never point the probe at `pulsr_development`.

### Formatting

The 80-column target applies to all maintained files, including C#, JavaScript,
SCSS, Razor, configuration and documentation. `.editorconfig` declares it in
the global section so editors apply the same ruler regardless of language.
Wrap expressions, attributes and prose safely. Do not hand-format generated
output or change literal contents, URLs or indivisible identifiers to fit.

#### C#

Run `dotnet tool restore`, then `dotnet csharpier Client Server Client.Tests
Server.Tests tools` from the repository root. Use the same paths with `--check`
for verification. The pinned tool uses `.csharpierrc.json`: two-space indentation
and an 80-column print width. `.editorconfig` gives editors the same defaults.
Generated sources are excluded by the formatter. Preserve literal text, URLs and
indivisible identifiers even when they exceed the print-width target.

Use namespace imports instead of repeating fully qualified type names in method
bodies or signatures. Prefer a meaningful alias when two types have the same name.
For a repository-wide semantic cleanup, run
`node scripts/artifacts.mjs run scratch -- dotnet run --project tools/CodeStyle
--artifacts-path '{artifacts}' -- REPOSITORY_DIRECTORY` before formatting. The
helper uses Roslyn from the installed .NET SDK, resolves names in their actual
projects and rejects new compiler errors before writing, while reporting any
pre-existing compiler errors separately. Run `bash test.sh all` and a
strict Client build after a broad cleanup; formatting does not authorize a deploy.
The helper's `--comments REPOSITORY_DIRECTORY` mode wraps standalone C# comments
at the same width using syntax trivia, without touching strings or XML comments.

### Application commands

JavaScript, TypeScript, SCSS, build scripts and browser fixtures use pinned
Prettier 3.6.2 with the same 80-column target, two spaces and single quotes.
Run `npm run format --prefix Client` to format these maintained files and
`npm run format:check --prefix Client` to verify them. The release gate rejects
formatting failures before building. String contents, URLs and regex literals
remain intact even when indivisible lines exceed the target.

Run `npm ci --prefix Client` before the first client build. Sass and deck.gl versions
are pinned by package-lock.json. `dotnet build pulsartms.slnx` builds changed SCSS and
GPU sources. Use `npm run styles:watch --prefix Client` for style development.
Never edit generated CSS, source maps, or files under `wwwroot/js/generated`.

Run `bash test.sh` (or an affected group from `docs/testing.md`). It includes both
.NET test assemblies and isolates their build artifacts from the running client.
Do not rebuild the development server's own `bin/Debug` output while expecting its
cached Blazor framework manifest to stay valid; restart that server after a direct
client rebuild. Release checks use `bash verify-release.sh` and a fresh publish tree.
Use the [managed artifact runner](artifact-retention.md) for other isolated builds
and diagnostics. Browser/release outputs now have automatic bounded retention;
do not create another permanent output directory for each iteration.

The generated JavaScript and CSS under `Client/wwwroot` are also shared with the
development server. After their contents change, stop the client, rebuild the
Client project and restart it before reloading browser tabs. A JavaScript-only
build does not regenerate the running server's import map or integrity metadata;
its old fingerprinted URLs can otherwise serve new bytes and fail browser integrity
checks. Isolated test intermediates do not isolate these generated source assets.

Start the API with `dotnet run --project Server/API/API.csproj` (port 5086)
and the client with `dotnet run --project Client/Client.csproj` (port 5067).
Use a normal Debug Client build for this split-origin local setup. Its embedded
WASM boot configuration must declare `applicationEnvironment: Development`, which
selects the API on port 5086. A published Release artifact defaults to Production
and calls same-origin `/api`; serving it with DevServer alone does not supply the
production API rewrite. Do not point DevServer at a virtual DLL in publish output
and assume the server's Development environment changes the embedded WASM setting.
For isolated builds, pass `--artifacts-path` to `dotnet build` and launch DevServer
against the resulting real Client DLL and its adjacent static-asset manifests.
Database migrations run only when Database:ApplyMigrations is enabled. Do not
enable this against a shared database without reviewing the pending migrations.

## Configuration and access

Development uses .NET User Secrets ID `pulsartms-api-local`. Production uses
environment configuration or Secret Manager; it does not load User Secrets.
Nested environment keys use double underscores.
The existing local secret store moved with the technical rename from
`amftms-api-local`; other checkouts must migrate that directory before starting
the API. See [product naming and compatibility](product-branding.md).

Required integration settings include ConnectionStrings:DefaultConnection,
Samsara:ApiToken, TorqueAI:BaseUrl, TorqueAI:ApiKey, GooglePlaces:ApiKey and
MediatR:LicenseKey. Gmail additionally needs ClientId, ClientSecret and RefreshToken.

The Admin policy reads the active account's persisted role through IUserRoleService.
Legacy accounts without an explicit role retain the existing Admin compatibility
behavior. Identity IDs, not email addresses, identify callers. User management,
settings writes and manual fuel operations require Admin. Reading the load-number
display prefix is available to all authenticated users; fuel settings remain
Admin-only. Handlers prohibit deleting,
deactivating or demoting the caller's own account. ICurrentUser is scoped and stores
no profile or role cache.

Removing secrets from files does not remove them from repository history or
revoke provider credentials. Rotate exposed credentials separately.

## Fuel import

One email and all eligible CSV attachments form a transaction. Stations and
discounts are saved before the email completion marker. A PostgreSQL advisory lock
serializes concurrent imports; failed attachments roll back the email transaction.
Previously marked emails are not automatically reimported. The Gmail reader searches
the last two days; historical repair needs explicit email/date selection.

Station lookup preparation happens before acquiring the message transaction/import
lock. An independent checkpoint reservation is committed before each provider
attempt, so process restarts and message rollback cannot erase its retry budget.
The three-minute per-station lease covers a 30-second bounded lookup. No-result
responses wait one hour; exceptions back off from 15 minutes to at most six hours.
Changed normalized name/city/region bypasses an older query's cooldown, but not an
active owner. A pending attempt or failure cooldown returns 503 and leaves the email
unconsumed. Successful prepared results can be reused for 24 hours after an import
rollback. Existing valid coordinates are preserved only for unchanged identifying
input; a changed query replaces address and point together. These checkpoint rows
use the existing schema; no additional migration is required. Each new attempt has
a persisted revision. The import validates it using its existing database
connection, skips stale location writes and still imports historical prices. This
is latest-prepared-lookup ordering, not an inferred newest-email policy. A cached
result keeps its revision; a legacy state without one requires a fresh attempt.

`POST /api/fuel/gmail-notifications` validates Google OIDC signature, audience,
verified service-account email and notification mailbox. Missing configuration
returns 503; invalid credentials return 401. Configure Pub/Sub authenticated push
with matching Gmail:PushAudience, Gmail:PushServiceAccountEmail and Gmail:MailboxEmail.
Manual `POST /api/fuel/import` requires Admin.

Admin `POST /api/fuel/gmail-watch/start` explicitly registers application-managed
daily renewal and bounded two-hour notification catch-up. It is not enabled by
deployment alone. All environments use configured non-interactive OAuth credentials;
follow [Gmail watch operations](../operations/gmail-watch.md) before activation or recovery.

The production subscription is `gmail-fuel-push`, using
`gmail-fuel-push@amftms.iam.gserviceaccount.com`. Its audience matches
`https://amftms-api-1055578316783.us-east4.run.app/api/fuel/gmail-notifications`.
Preserve these settings during deployment. The Pub/Sub service agent needs Token
Creator only on the dedicated push account.
CSV dates accept a single date or an inclusive range such as
`2026-09-06 to 2026-09-09`.

## Dispatch and map

TorqueAI supplies imported data during the transition to native Dispatch.
The full-page [load workspace](../features/dispatch-workspace.md) supports
editing existing loads. Import ownership and explicit native overrides keep
provider refreshes from overwriting dispatcher changes or confirmed execution.
The default import window is seven days before and after today, not full
history. Dispatch supports search, pagination, ordered stops and Fleet Map
links. Truck selection includes confirmed execution and stop-level assignments.

The production renderer uses one foreground deck.gl canvas for ordinary fuel
stations, routes, stops, trucks and labels. Details are fixed HTML cards.
Recommendations retain native distance annotations. Roadmap/hybrid switching
happens after idle at zoom 15; tilt and rotation are disabled.
See [map boundaries](../architecture/javascript.md) and [renderer notes](../archive/2026-09/map-gpu-prototype.md).
SCSS uses base/global/layouts/components/pages; UI partials consume the public
`base` module. See [style ownership](../architecture/styles.md).

Savings are computed as retail minus discounted price. IFTA rates prefer the
selected date's quarter and fall back only to the previous quarter for a missing
region/currency. Do not substitute older or future rates.

## Publishing

Use the deployment scripts after tests pass. Publish the client into an empty
output directory: incremental publishing can retain deleted JavaScript files.
Preserve production configuration, verify the ready revision and hosting assets,
and check `/api/health/live`. Do not interpret liveness as an integration test.

See [synchronization](../features/synchronization.md) and [diagnostics](../operations/diagnostics.md)
for scheduling, recovery, logging and known verification limits.
