# Client release — 2026-09-13

The user approved publication after the compact telemetry/HOS layout change.
The release scope is the current Client on Firebase Hosting project `amftms`.
No Cloud Run deployment, migration or database change was attempted.

## Verification

- `bash verify-release.sh` passed: 494 Node tests, 805 Client tests and
  1,663 Server tests; the strict solution build had no warnings or errors.
- Published integrity verification passed for 264 assets and seven JavaScript
  entry-point dependency graphs.
- The exact release artifact passed all 12 Fleet-only hours/inspector browser
  cases, including 320–767px readings at 100%/200% text in both themes.
- The exact release artifact passed eight page-transition browser cases.
- Both browser reports contain no reported errors or unexpected requests.

The staged artifact is `artifacts/managed/release-ZCcicX/publish/wwwroot`.
The release directory is pinned with `.keep` as published release evidence.
Browser evidence is in `browser-hours-forecast-t4UdfI` and `browser-ui-bxYYFB`
under `artifacts/managed`; those unpinned reports remain subject to retention.

## Initial publication blocked

Firebase's read-only Hosting inventory rejected the expired login and requested
`firebase login --reauth`. The existing gcloud login also could not refresh
without interactive reauthentication. No deployment command was executed in
that initial attempt.

Before publication, `https://tms.amfcarrier.com/` returned HTTP 200 with
`Cache-Control: no-cache` and CSS version `f33407acb8bff769`. The prepared
artifact's CSS version was `336153587b77b736`, not yet the live version then.

## Publication completed

After the user requested another attempt, the existing Firebase login passed
the read-only Hosting inventory. Integrity verification of the same staged
artifact passed again before deployment.

Firebase Hosting accepted the exact `release-ZCcicX/publish/wwwroot` directory
with `--only hosting --project amftms --non-interactive`. Published version:
`projects/1055578316783/sites/amftms/versions/f4e50f2ad6fb0fdf`.

Read-only checks on both `https://tms.amfcarrier.com` and
`https://amftms.web.app` confirmed HTTP 200 and matching staged SHA-256 hashes
for `/`, `/fleet/map`, the versioned stylesheet, `Client.6j0mp5z2d3.wasm`,
the Fleet Map module and GPU renderer. The live CSS version is now
`336153587b77b736`.

HTML, CSS and unversioned JavaScript returned `Cache-Control: no-cache`.
The fingerprinted Client WASM returned one-year immutable caching. API, Cloud
Run configuration and database remained unchanged.

## Verification limits

The general multi-page offline smoke and authenticated/live-provider checks were
not run for this attempt. Browser fixtures do not establish real provider or
production calculation correctness. No isolated PostgreSQL fixture was
available; real PostgreSQL checks were not run.

## Mobile inspector follow-up published at 17:32 UTC

The user explicitly approved this Client publication. The mobile card now uses
one disclosure, two-by-two telemetry beside one HOS row, a full-width address,
and a natural height ending at the load link. Retained route facts are scrollable.
Temperature defaults to Celsius unless Fahrenheit is explicitly selected.

- The complete release gate passed: 520 Node, 822 Client and 1,694 Server tests;
  strict Release solution build with zero warnings/errors; 267 verified assets
  and seven generated JavaScript dependency graphs.
- Offline UI checks passed 52 page scenarios across 12 width/theme/text-size
  combinations. Evidence: `artifacts/managed/browser-ui-v5ZygV`.
- The first attempt stopped before deployment because bundled Chromium was
  absent. The installed Chrome was used on subsequent attempts. The second
  stopped at an outdated test expecting temperature Both. The assertion now
  independently checks Celsius temperature and Both distance defaults. No
  application behavior was changed to accommodate that test.
- The exact final artifact is
  `artifacts/managed/release-PzsLZM/publish/wwwroot`; its release directory and
  the successful UI report are pinned with `.keep`.
- Firebase live version: `3dd5a649959f594a`; live release:
  `1789320756229000`, completed at `2026-09-13T17:32:36.229Z`.
  The preceding version was `b640e0c08e85157c` for rollback reference.
- Both the custom domain and `amftms.web.app` returned HTTP 200. Twenty-two
  assets per hostname, including HTML, CSS, generated JavaScript and Client
  WASM, matched the staged SHA-256 bytes. `/fleet/map` matched staged HTML.
  Entry HTML/CSS revalidate; fingerprinted WASM retains immutable caching.
- A fresh anonymous Chrome context booted the production application and
  reached Login with no runtime errors. Authenticated production map/provider
  interaction was not tested. Earlier local Fleet evidence remains in
  `artifacts/managed/browser-hours-forecast-sFuC4s`.

No API deployment, database writes or migrations were performed. Real
PostgreSQL execution and production performance measurements were not run.
