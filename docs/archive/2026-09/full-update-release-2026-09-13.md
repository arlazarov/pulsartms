# Full update release — September 13, 2026

The user explicitly approved publishing the entire current update. The scope
includes the API, Client, account appearance and units, fuel-card sizing and
the known-Cycle forecast fallback for continuous partial HOS history.

## Release checks

- Final local `bash verify-release.sh` with `PULSARTMS_RELEASE_UI=1` passed:
  514 Node tests, 821 Client tests and 1,694 Server tests. The strict solution
  build completed with zero warnings and errors.
- Integrity verification passed for 267 published assets and seven generated
  JavaScript entry-point dependency graphs.
- The general offline browser smoke passed 12 viewport/theme/text-size cases:
  52 page checks, no reported geometry failures, browser errors or unexpected
  requests. Personal settings heading and label colors are checked as well.
- Account appearance/units checks passed at 1440px and 390px, including saves,
  account isolation and restoration in a fresh browser context.
- Fuel-station sizing passed 16 cases; page transitions passed eight cases.
- `git diff --check` passed.

The final verified Client is
`artifacts/managed/release-zX9TJt/publish/wwwroot`. Its directory is pinned.
Pinned browser evidence under `artifacts/managed`:

- `browser-ui-Yztnmh`: general UI.
- `browser-ui-TriCWW`: personal preferences.
- `browser-station-popup-UxaYJW`: fuel-card sizing.
- `browser-ui-8oVMM8`: page transitions.

## Issues caught before publication

The general smoke still expected shared unit controls and the removed Trucks
filter. Its read-only API fixtures also lacked separate HOS and enrichment
responses. The fixture now follows the current UI and endpoints; personal
preference writes remain covered by the separate account smoke.

Dark Settings cards could retain light-theme inherited text during cold loading.
Settings surfaces now explicitly pair their background with semantic text.
The final mobile dark screenshot was visually inspected after this correction.

Cloud Build `606749c6-9fdd-4ef8-b4f4-f1bbee2dc65f` stopped before deployment:
one Dispatch forecast component test exceeded bUnit's default one-second wait.
Its seven-test class passed locally. The affected test now waits for its planning
request to start, then allows five seconds for rendering, consistent with its
neighboring test. Assertions about forecast ownership were not changed.
The complete local suite passed again after that test correction.

## Schema review

A read-only migration inventory found exactly two pending additive migrations:

- `20260913135741_AddUserTheme`: per-account theme, default Light.
- `20260913142839_AddUserDisplayUnits`: personal temperature and distance units,
  initially copied from existing fleet preferences.

Existing shared columns are retained for rollout compatibility. Production
startup applies pending migrations before serving HTTP or starting workers.

## Deployment

Cloud Build `d7ecb28a-a0cb-4d44-821a-9b893f4f20be` succeeded, including
821 Client tests and 1,694 Server tests in Linux. The API was deployed by
immutable digest to Cloud Run revision `amftms-api-00108-vmn`, which became
Ready and received 100% of traffic at 15:06 UTC.

Image digest:
`sha256:a728054d8c0a4b1a207a2c8d5b4878746754c898e205ba9675582391d5173f94`.

The read-only EF migration inventory after startup confirmed both reviewed
migrations were applied, with none pending. The new revision's severity ERROR
or higher log query returned no entries during the initial post-deploy check.

Firebase Hosting published the exact verified Client artifact at 15:07 UTC.
Live version: `projects/amftms/sites/amftms/versions/b640e0c08e85157c`.
The final Client includes the Settings text contrast correction, made after
the server upload; no server source changed between these artifacts.

Read-only HTTP checks on `https://tms.amfcarrier.com` and
`https://amftms.web.app` confirmed matching staged SHA-256 hashes and HTTP 200
for `/`, `/fleet/map`, `/settings/personal`, the versioned stylesheet,
`Client.3hkdl0s9p3.wasm`, Fleet Map and appearance entry modules.
The stylesheet version is `20dddeca7da9ac04`.
HTML, CSS and unhashed modules revalidate with `no-cache`; fingerprinted WASM
uses one-year immutable caching. Both origins returned HTTP 200 for API
liveness and the expected HTTP 401 for unauthenticated personal settings.

## Limits

Browser scenarios use synthetic API responses and no live provider writes.
They do not prove live Google/Samsara correctness or production performance.
No safe isolated PostgreSQL fixture was available; PostgreSQL integration
execution was not run against the application database. Migration inventory
and approved deployment are operational checks, not disposable database tests.
The local SDK reported publishing without the optional wasm-tools workload.
No authenticated production end-to-end or live GPU/provider audit was run.
