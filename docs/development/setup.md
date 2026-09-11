# Development and operations

Read [architecture](../ARCHITECTURE.md), [UI controls](../ui-controls.md), and the root AGENTS.md first.

## Build and run

Run `npm ci --prefix Client` before the first client build. Sass and deck.gl versions
are pinned by package-lock.json. `dotnet build AMFTMS.slnx` builds changed SCSS and
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

Development uses .NET User Secrets ID `amftms-api-local`. Production uses
environment configuration or Secret Manager; it does not load User Secrets.
Nested environment keys use double underscores.

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

Torque is the source of truth; local dispatch editing is not implemented.
The default import window is seven days before and after today, not full history.
Dispatch supports search, pagination, details, stops and links to the fleet map.
Truck dispatch selection includes stop-level assignments.

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
