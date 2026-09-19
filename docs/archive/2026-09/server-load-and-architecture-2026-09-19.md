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
