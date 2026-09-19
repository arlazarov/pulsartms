# September 19 server load reduction and architecture hardening

Branch `claude/intelligent-rubin-2tsaw1`, commits `b67f3f5` through `2c9fbe5`
on top of `fd5dd2d`. Every commit passed `bash test.sh all` locally and the
GitHub `Verify` workflow (build, both .NET test assemblies, Node suites, release
artifact check and the offline UI smoke), except where noted below.

## What changed

- Hosting: Brotli/Gzip response compression, EF Core SQL logging at `Warning`
  outside Development, invariant globalization, workstation concurrent GC,
  ReadyToRun publish for `linux-x64`, immutable Firebase caching for hashed
  framework and chunk assets.
- Telemetry polls: `GET /api/fleet/locations` carries a weak `ETag` per published
  snapshot and answers matching `If-None-Match` with `304`; the board polls
  `?points=false`; trail points carry only movement fields.
- Board reads: planning, fuel, preview, synchronization and ETA services call
  `IDispatchBoardReader` directly instead of sending `GetDispatchBoardQuery`
  through MediatR; `DispatchBoardService` adds ETA enrichment for HTTP reads.
  `MediatorUsageTests` lists the remaining `ISender` debt.
- ETA chain descriptions are memoized per truck across board, dispatch, profile,
  route and chain cache generations for at most sixty seconds.
- Hidden browser tabs poll every sixty seconds instead of ten.
- API JSON uses source-generated `ApiJsonContext` metadata with a coverage and
  equivalence test.
- `ReadCache.GetSharedAsync` shares immutable fuel station lists without a JSON
  round trip per hit.
- Planning polls are `GET` requests with a body-digest weak `ETag`; the Client
  sends the tag itself and reuses its cached plan on `304`. `POST` remains for
  older Clients.
- Metrics export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; a k6
  polling scenario and a load-testing guide exist.
- `ApiContractTests` drive the real MVC pipeline in process.
- `FuelDiscounts:Source` selects the fuel discount provider (`bvd-gmail` or
  `none`), so customers without a fuel card run no Gmail worker.
- Twenty more Fleet Map helper modules are type-checked.
- Telemetry polls are held on the server (`wait=25`) until the next published
  snapshot, so quiet fleets cost one request per wait and changes reach open tabs
  within a second; the deploy script sets `--concurrency 250` for the held slots.
  This is the push-style delivery the plan called SSE, done over ordinary requests
  because Firebase Hosting rewrites to Cloud Run buffer streamed responses and
  time out at 60 seconds.
- Stale `Synchronization.Services` usings left over from the `ReadCache` move were
  removed from Users and Fuel, so Users no longer references another feature.
- Every static gate moved to the `ProcessGates` DI singleton (per-key stripes,
  single slots and the two-slot fuel search budget); `SynchronizationGates` is an
  injected singleton. Both test projects carry an explicit `xunit.runner.json`.
  `ProcessStateTests` leaves only the per-process request counter behind the
  diagnostics endpoint, and `FeatureDependencyTests` freezes the cross-feature
  reference map; both lists may only shrink.
- `FuelSearchGeometryAllocationTests` (25001 points per leg) failed once in a
  local full run on its per-thread allocation bound and passed on every rerun;
  it measures allocations under parallel collections and may need a wider bound
  if it recurs in CI.

## CI failures seen and fixed on the branch

- Run 10 failed on `SettingsComponentTests.SavingFuelPreferencesDoesNotSubmitOrResetAnIntegrationDraft`,
  a test race between the integration and planning settings loads; the test now
  waits for the form like its siblings.
- Runs 11 and 12 failed the offline UI smoke because its fixture only answered
  `POST` planning polls; the fixture now answers `GET` too. The hours-forecast
  smoke also had a lowercase `scripts/` path that only resolved on
  case-insensitive file systems.

## Not measured

No production or staging load measurement was taken. Reduced request counts,
smaller bodies and skipped serialization are established by tests, not by
latency, CPU or egress numbers. The k6 scenario has no baseline yet and the
OTLP export has not been pointed at a collector. The planning handler still
runs in full on every poll; only the response transfer is skipped on `304`.
The in-process `LoadProbe` harness was not rerun.

## Worker split: assessed, not built

Moving the synchronization, ETA, planning and Gmail workers to a second Cloud Run
service would need the telemetry snapshot, ETA memory and planning caches to move
out of process (a shared store or Redis), because `ServerTelemetry`,
`EtaMemory`, `FuelPlanMemory` and `ReadCache` are per instance and the
synchronization checkpoint persists cursors, not the snapshot. That is a separate
project with its own measurements; the single-instance `--max-instances 1`
deployment does not need it at the current fleet size.
