# Inspector and Full tank release — September 12, 2026

## Scope

The truck inspector keeps the trailer beside the truck/driver identity, current
duty below telemetry, Next recap below the unchanged HOS clocks, and the dispatch
link beside the GPS information. Details reveals only the lower load section.
Remaining distance uses separate label, miles and kilometers rows; ordinary
pending address/forecast placeholders preserve the loaded geometry.

Full tank remains a target across prepared slider choices, save and reopening.
An earlier purchase changes the later full purchase to its available headroom;
the adjusted partial-purchase floor is not imposed on a full target. Existing
manual replay safety checks remain in effect. Financial calculations remain on
the server and changing a prepared slider quantity does not call the route provider.

## Client deployment and verification

- Firebase Hosting: `amftms`, https://amftms.web.app.
- Published artifact: `artifacts/managed/release-ZhWJzl/publish/wwwroot`.
- The full local release gate passed: 1,613 Server, 741 Client and 463 Node tests,
  strict builds, typed JavaScript and integrity checks for 264 assets and seven
  JavaScript dependency graphs.
- Offline UI smoke passed across both themes, narrow/wide screens and enlarged
  root text. Evidence: `artifacts/managed/browser-ui-Ua3jjo`.
- The exact staged artifact passed all ten Fleet inspector/HOS scenarios with no
  browser errors or unexpected requests. Evidence:
  `artifacts/managed/browser-hours-forecast-4Dxe9y`.
- The public index, stylesheet and Client WASM returned HTTP 200 and matched the
  staged bytes; index and stylesheet retained `Cache-Control: no-cache`.

## First cloud attempt and test synchronization

Cloud Build `2a235601-249c-46b7-9b1f-726c525b8f31` failed before deployment.
`DispatchBatchRefreshTests.AStopEditInvalidatesOnlyItsTruckAndKeepsAnUnrelatedWarmMapPlan`
timed out waiting for the initial planning snapshot on the shared cloud runner.
The test now awaits its planning-request signal before a bounded five-second
render wait. Cache invalidation/preservation assertions are unchanged; no
production implementation, test filter or architecture exception was changed.

The Dispatch dependency group then passed (598 Server and 443 Client tests, six
Dispatch Node tests and 46 Node architecture tests). The complete local release
gate passed again with the same full-suite counts. All 264 files in its
`release-rR6TmM/publish/wwwroot` artifact matched the published client, so no second
Firebase publication was needed for the test-only follow-up.

The second cloud attempt, `968566cc-a5bf-4ac4-930f-2f21028fdbb4`, encountered an
initial-render timeout in a different test,
`SettingsComponentTests.SavingPriceBasisPreservesTheServerProvidedHiddenPreferences`.
Its failure reported zero assertion checks and one component render. Settings
tests and their limits were left unchanged. A third attempt used the same complete
Cloud Build configuration with a one-off `--machine-type=e2-highcpu-8` override;
this does not change the Cloud Run service or the repository's default build type.

## Successful server deployment

- Cloud Build: `619f0ade-81c5-4e3c-ab89-c0791269a42c`, `SUCCESS`, finished
  `2026-09-12T22:06:39.262844Z` using `E2_HIGHCPU_8`.
- The unchanged full cloud gate passed: 463 Node, 741 Client and 1,613 Server
  tests, strict builds and staged asset verification. Build compilation took
  31.35 seconds in this run, versus approximately 140 seconds in the first two
  attempts; these are build observations, not application performance results.
- Verified API image:
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:fd78ecb057d6f39a45815c2d8e246de7226b1230c1685c254ab7cb1ed93df661`.
- Cloud Run revision `amftms-api-00107-kz5` became Ready at
  `2026-09-12T22:07:17.561844Z` and received 100% of ordinary traffic. Previous
  ready revision: `amftms-api-00106-rhx`.
- The successful image digest was deployed with the existing wrapper's service,
  region, port and scaling/CPU arguments; no mutable image tag was deployed.
- Post-rollout liveness returned HTTP 200 / `Healthy` directly and through the
  Firebase `/api` rewrite.

## Limitations

No new migration was introduced in this release. PostgreSQL fixture checks,
authenticated production workflows and live provider calculations were not run.
Liveness and public asset checks are startup/release evidence, not end-to-end or
production performance measurements. The existing `amftms` cloud project remains
active; no database move, domain change or integration cutover was performed.
No Git commit or push was performed.
