# Release and performance review

## Execution environment

Docker is permitted for builds and deployment. The existing Docker-based Cloud
Build/Cloud Run release path remains supported; no replacement is required by the
tooling policy. The restriction applies to starting local SQL database servers in
containers, including disposable verification fixtures. See `docs/testing.md`.

## Release changes

The [core storage cutover](core-storage-cutover.md) was applied on September 18,
2026 (UTC). Its guarded SQL was rehearsed by the isolated PostgreSQL probe.
Do not repeat the operational reset; see the [production record][core-cutover].

[core-cutover]: ../archive/2026-09/core-cutover-2026-09-18.md

The user permits replacement of operational data while preserving the complete
identity boundary. The [core specification](../architecture/core-rebuild.md)
owns scope and the remaining implementation gates. Local verification does not
complete the rebuild or authorize deployment.

The applied `20260917055902_RebuildExecutionStorage` migration replaces the
unapplied intermediate September 17 migrations. It requires an explicit prior
operational reset, removes native stop JSON and duplicate transfer-visit storage,
and creates typed accepted stops and immutable revision history. It does not
migrate old business data or alter users. Downgrade rejects populated new work;
use a forward repair or reviewed recovery instead of discarding accepted facts.

The same migration adds the on-demand planning queue. Workers require
that table at startup. Downgrade checks pending request versions and accepted
execution before deleting any tables. Keep this transition in one migration:
EF may commit individual migration steps before a later step rejects a downgrade.
It was applied as part of the compatible working-database cutover.

`tools/CoreMigrationProbe` verifies the clean transition in a synthetic isolated
PostgreSQL fixture, with exact protected-data comparison, password/role checks,
new accepted work and rejection of unsafe migration paths. It never applies
migrations or cleanup to the working database. Source bootstrap and final
compatible-release verification remain required before production cutover.

The native execution schema is not rolling-compatible with earlier binaries.
For this transition, stop all old readers/writers, including tagged revisions
and local APIs sharing the database, before applying migrations. Deploy without
traffic, direct all traffic to the compatible revision, then resume scaling.
Do not restore old traffic tags against the new schema. The applied September 14
transition and recovery limitations are recorded in [the release evidence][native].

[native]: ../archive/2026-09/execution-release-2026-09-14.md

Production applies pending EF migrations before starting HTTP or hosted services. A migration failure aborts startup. Database:ApplyMigrations can disable this when migrations are executed separately. The database account needs schema migration permissions. Existing deployments must use additive schema changes compatible with the previous revision during rollout.

Data Protection keys are stored in PostgreSQL with application name AMFTMS. The first deployment of this change can require users to sign in again because existing container keys are not imported. Subsequent container replacements reuse the database key ring. Restrict access to the key table and database backups; application-level encryption of key XML is not configured.

Client refresh requests are serialized per instance, reuse an already refreshed
token and retain credentials on temporary refresh failures. Session migration and
compare-and-set storage use an origin-wide Web Lock; login identity prevents a
stale refresh or logout from replacing a different login. This does not serialize
HTTP refresh requests globally across tabs.

`bash verify-release.sh` is the shared offline release gate. It installs pinned Node
dependencies, checks the typed JavaScript boundary, builds styles and JavaScript,
runs all Node suites (including style contracts and architecture), builds the whole
solution with warnings as errors, and runs both .NET test assemblies unfiltered.
It publishes the Client into a new ignored
`artifacts/managed/release-*/publish` directory,
checks every published static asset against the SDK's SHA-256 manifest (including
compressed variants), checks bootstrap/import-map references, and resolves the maintained
generated JavaScript entry-point dependency graphs. No old publish output is reused
or deleted. The verified directory is printed and retained for inspection.

GitHub Actions runs the complete gate plus the offline UI smoke and retains its
fixture-only screenshots/report. Cloud Build uses the gate's `node`, `dotnet` and
`artifact` phases in the corresponding toolchain containers before building the API
image. These phases are CI composition points, not standalone release approval.
The Node step shares its Node executable with the .NET step through a dedicated
Cloud Build volume. Test-runner architecture checks execute artifact retention,
which requires Node even when .NET tests stub their nested dotnet/npm commands.
The API runtime image does not receive this build-only toolchain.
Cloud Build uses `E2_HIGHCPU_8` for the complete build and test gate. This is
build-worker sizing only; it does not change Cloud Run instance resources.
`deploy-client.sh` runs the gate and gives Firebase the exact verified staged
`wwwroot` through `--public`. Existing Firebase rewrites and project selection stay
unchanged. Neither deployment continues after a failed gate.
The style build stamps `css/main.css?v=<content hash>` into `index.html`; the Client
build also refreshes this stamp when SCSS compilation is incremental. Firebase
responses revalidate entry HTML, configuration, CSS and unhashed module aliases
with `Cache-Control: no-cache`. Content-fingerprinted framework/module files and
generated hashed chunks use one-year immutable caching; the narrower rules follow
the default header rule. Verify final response headers after an approved deployment.
Publish the
matching index and CSS together; do not manually copy one file from an older build.

Cloud Build tags the API with its unique `$BUILD_ID`. `deploy-server.sh` waits for
that build, verifies its successful result and expected image name, and deploys
the returned `sha256` digest, never a shared mutable `latest` tag. It creates a
build-specific revision without traffic, verifies its Ready condition and image
digest, then explicitly moves 100% of traffic to that revision. It confirms both
requested and observed traffic, including the observed service generation, before
reporting success. Invalid identity, readiness or traffic results fail the command;
they never trigger an automatic rollback over another operator's changes. It
prints the build ID, digest and serving revision for release records and rollback
selection. Image retention policies must retain revisions needed for rollback.
No cleanup policy is changed.

The deployment wrapper requests 1 GiB of API memory. The September 21 revision
hit its former 512 MiB limit during normal operation; this setting addresses that
observed termination, not a measured upper bound on memory or a scaling claim.

Inspect actual traffic after deployment. A previously pinned revision can retain
100% of traffic while a new image is created successfully. The wrapper explicitly
handles this case; when deploying manually, verify the new revision's image digest
and route traffic to that exact revision. Do not publish a dependent Client until
the new API is ready and old
background writers have drained. A deployment command succeeding is not proof
that the new revision is serving requests.

Browser verification is deliberately opt-in: with a local API/client origin already
running, use `PULSARTMS_RELEASE_BROWSER=1 MAP_TEST_URL=http://localhost:5067 bash
verify-release.sh` (or the same environment on `deploy-client.sh`). The visible
Chrome probe serves static resources from that exact staged artifact, forwards
`/api` to the local origin, and leaves provider requests unchanged. Sign in manually
if prompted. This can call live map providers; it is not part of the offline gate.
It covers map/SPA lifecycle, not all responsive layouts or production performance.
Review relevant visual scenarios separately; keep heap snapshots local and private.

The separate offline UI smoke needs no backend, login or provider access. Set
`PULSARTMS_RELEASE_UI=1` on the gate after installing Playwright Chromium with `npx
playwright install chromium` from Client. To use an installed Chrome locally, also
set `UI_TEST_BROWSER_CHANNEL=chrome`. It boots the staged WASM with deterministic
read-only API fixtures, blocks all network/writes, and substitutes only the map
provider module. It checks six primary/form pages at 1440px and 390px, in both
themes and at 100%/200% root font size, with heading alignment, control bounds and
basic interactions, including personal settings and themed text. Its managed
`artifacts/managed/browser-ui-*` screenshots contain fixture data only.
This is not map/GPU acceptance or a browser zoom test.

Release controls use `PULSARTMS_RELEASE_DIR`, `PULSARTMS_RELEASE_UI` and
`PULSARTMS_RELEASE_BROWSER`. The server deployment wrapper accepts
`PULSARTMS_DEPLOY_ENV_FILE` for an explicitly supplied environment file. Each
wrapper accepts its corresponding `AMFTMS_*` legacy variable only when the
canonical variable is unset; explicit canonical empty values and `0` flags win.
Cloud project, registry and service identities remain unchanged by these local
tooling names.

## Evidence and remaining verification

Dated results and unmeasured scaling work belong in the
[release review archive](../archive/2026-09/release-verification.md). Later deployment
identities and verification results are recorded in the
[stabilization history](../archive/2026-09/stabilization-work.md).
A past successful gate is not approval or verification for a new release.
