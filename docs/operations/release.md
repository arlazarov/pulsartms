# Release and performance review

## Execution environment

Docker is permitted for builds and deployment. The existing Docker-based Cloud
Build/Cloud Run release path remains supported; no replacement is required by the
tooling policy. The restriction applies to starting local SQL database servers in
containers, including disposable verification fixtures. See `docs/testing.md`.

## Release changes

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
It publishes the Client into a new ignored `artifacts/release.*/publish` directory,
checks every published static asset against the SDK's SHA-256 manifest (including
compressed variants), checks bootstrap/import-map references, and resolves the maintained
generated JavaScript entry-point dependency graphs. No old publish output is reused
or deleted. The verified directory is printed and retained for inspection.

GitHub Actions runs the complete gate plus the offline UI smoke and retains its
fixture-only screenshots/report. Cloud Build uses the gate's `node`, `dotnet` and
`artifact` phases in the corresponding toolchain containers before building the API
image. These phases are CI composition points, not standalone release approval.
`deploy-client.sh` runs the gate and gives Firebase the exact verified staged
`wwwroot` through `--public`. Existing Firebase rewrites and project selection stay
unchanged. Neither deployment continues after a failed gate.
The style build stamps `css/main.css?v=<content hash>` into `index.html`; the Client
build also refreshes this stamp when SCSS compilation is incremental. Firebase
responses revalidate `index.html`, CSS and the generated JavaScript entry points
with `Cache-Control: no-cache`. Content-fingerprinted files (`_framework/**` and
`js/generated/chunks/**`) are served as immutable for one year; the later header
rule wins for those paths because Firebase applies matching rules in order. Any
new asset whose name does not change with its content must stay under the
revalidated set. Publish the matching index and CSS together; do not manually copy
one file from an older build.

The API image is published framework-dependent for `linux-x64` with ReadyToRun
precompilation, invariant globalization and workstation GC. ReadyToRun applies only
when a runtime identifier is given, so `dotnet build`/`dotnet test` and the release
gate are unaffected. Formatting and comparisons already use invariant culture
explicitly; adding culture-specific formatting to the server requires reverting
`InvariantGlobalization`.

The default deployment is one Cloud Run service with `--max-instances 1` in role
`All`. To split request serving from background work, deploy the same image twice:
`amftms-workers` with `Hosting__Role=Workers`, `Database__ApplyMigrations=true`,
`--max-instances 1` and a lower `Synchronization__CheckpointSeconds`; and the
request tier with `Hosting__Role=Api`, `Database__ApplyMigrations=false` and as many
instances as needed. Deploy the workers service first so migrations and the lease
exist before request instances start following the checkpoint. See
[synchronization](../features/synchronization.md) for the staleness bound of `Api`.

Cloud Build tags the API with its unique `$BUILD_ID`. `deploy-server.sh` waits for
that build, verifies its successful result and expected image name, and deploys
the returned `sha256` digest, never a shared mutable `latest` tag. It prints the
build ID and digest for release records and rollback selection. Image retention
policies must retain revisions needed for rollback. No cleanup policy is changed.

Browser verification is deliberately opt-in: with a local API/client origin already
running, use `AMFTMS_RELEASE_BROWSER=1 MAP_TEST_URL=http://localhost:5067 bash
verify-release.sh` (or the same environment on `deploy-client.sh`). The visible
Chrome probe serves static resources from that exact staged artifact, forwards
`/api` to the local origin, and leaves provider requests unchanged. Sign in manually
if prompted. This can call live map providers; it is not part of the offline gate.
It covers map/SPA lifecycle, not all responsive layouts or production performance.
Review relevant visual scenarios separately; keep heap snapshots local and private.

The separate offline UI smoke needs no backend, login or provider access. Set
`AMFTMS_RELEASE_UI=1` on the gate after installing Playwright Chromium with `npx
playwright install chromium` from Client. To use an installed Chrome locally, also
set `UI_TEST_BROWSER_CHANNEL=chrome`. It boots the staged WASM with deterministic
read-only API fixtures, blocks all network/writes, and substitutes only the map
provider module. It checks five primary/form pages at 1440px and 390px, in both
themes and at 100%/200% root font size, with heading alignment, control bounds and
basic interactions. Its ignored `Client/test-results/ui-smoke` screenshots contain
fixture data only. This is not map/GPU acceptance or a browser zoom test.

## Evidence and remaining verification

Dated results and unmeasured scaling work belong in the
[release review archive](../archive/2026-09/release-verification.md). Later deployment
identities and verification results are recorded in the
[stabilization history](../archive/2026-09/stabilization-work.md).
A past successful gate is not approval or verification for a new release.
