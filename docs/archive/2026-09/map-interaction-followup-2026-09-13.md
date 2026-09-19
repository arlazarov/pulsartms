# Map interaction follow-up — September 13, 2026

## Earlier local changes

- Single-day ordinary fuel quotes use content-driven width. Title, Back to truck
  and Close share a row when space permits; enlarged text can wrap. Comparison
  quotes and planned purchases retain their own wider layouts.
- A blank-map click returns a stop/fuel inspector to its selected truck, or
  closes it when no truck is selected. A truck-card dismissal uses the existing
  deselection path. Revision and truck identity reject obsolete callbacks.
- GPU markers and road strokes consume inspection clicks. Route editing keeps
  its separate click handling for selecting options and adding via points.
  Suspended inspectors cannot dismiss active editors or camera dialogs.
- Layer controls retain their geometry but conceal unconfirmed preference
  state. Saved choices become visible before provider initialization finishes.
  Missing, invalid or blocked storage retains the existing defaults.

## Earlier verification

- `bash test.sh map styles`: passed dependent Fleet and Architecture categories,
  including 253 Client and 125 Server tests, plus the required Node suites.
- Final `bash verify-release.sh`: 497 Node, 809 Client and 1,663 Server tests
  passed. Strict builds reported no warnings or errors; artifact verification
  checked 264 static assets and seven JavaScript entry graphs.
- Artifact: `artifacts/managed/release-mIWLnn/publish/wwwroot`.
- `stationQuoteSizingSmoke.mjs`: 16 cases passed against that artifact at
  320–1440px, both themes and 100%/200% text. Production popup code and CSS use
  synthetic quotes in a representative inspector shell.
- `mapToolbarSmoke.mjs`: 16 actual staged Blazor cases passed, including a held
  preference read, retained controls, unchanged toolbar bounds and restored
  choices. API and map-provider responses are synthetic and read-only.
- Final strict Debug Client build passed; localhost was restarted on port 5067.
  The browser-control runtime could not start, so the user's tab was not reloaded.
- Live Google picking, authenticated production telemetry and PostgreSQL checks
  were not run. Automated results do not establish full visual correctness or
  production performance.

## Previous API publication attempt

Cloud Build `69d1a6e9-d305-45c1-be4b-90d3dfbd4dec` failed its Client test gate:
`DispatchBatchRefreshTests` timed out waiting for twelve planning snapshots.
The remaining 804 Client and all 1,663 Server tests passed in that attempt.
This test was not weakened or skipped; the final local full gate passes it.

The deployment wrapper stopped before changing Cloud Run. The ready revision
remains `amftms-api-00107-kz5`. No new Firebase release was made in this follow-up,
and no migration was applied. Production fuel-price overview and outside-sensor
availability remain unverified after this failed API publication attempt.

## Subsequent local inspector changes

- Selecting a truck retains its road without requesting a route fit. Show route
  beside Follow fits the already-published remaining geometry and ends Follow.
  It does not request, recalculate or republish a route. Stale truck identities,
  completed routes, editors and disposed maps cannot trigger this action.
- Blank-map clicks only collapse the selected truck's Details. Repeated clicks
  retain the card and road; Close still performs deliberate deselection.
- Back replaces Close for stop/fuel inspectors with a selected truck. No empty
  controls group remains in the ordinary fuel/current-stop header.
- Appointment labels use intrinsic width and the same compact text gap as ETA,
  removing the previous eight-character reservation. Loading still retains the
  appointment row and surrounding groups; resolved label text can change width.
- The last telemetry reading has no trailing padding. A named narrow-container
  breakpoint reduces inner padding without shrinking telemetry or HOS sizes.

## Truck 54777 distance diagnosis

A targeted read-only transaction inspected the saved AMF1376 plan, version 10,
calculated at `2026-09-13T02:02:35.9656699Z`. No plan or stop was changed.

- Remaining route: 1,356.821 miles from the saved current-position origin,
  consisting of 0.638 miles to Webster pickup and 1,356.184 miles to delivery.
- Original planned distance: 1,580.382 miles retained in the plan metadata.
- The roughly 223-mile difference is not evidence that the truck travelled that
  distance. The available saved metadata does not establish the original origin.

The diagnostic tool now includes existing original/reference/leg distances in
its output. This is an operational read, not a PostgreSQL test fixture.

## Subsequent verification

- Final `bash verify-release.sh`: 500 Node, 809 Client and 1,663 Server tests
  passed, including architecture checks. Strict builds had no warnings/errors.
  Artifact verification checked 264 assets and seven JavaScript entry graphs.
- Final artifact: `artifacts/managed/release-Fx43iL/publish/wwwroot`.
  A final SCSS-only line wrap was recompiled; `cmp` confirmed the generated CSS
  is byte-identical to this verified artifact.
- Fleet-only `hoursForecastSmoke.mjs`: all 12 desktop/phone/theme cases passed,
  including 320–767px readings sweeps at 100%/200% text. It exercises production
  Blazor with synthetic read-only APIs and a map callback substitute. Output:
  `artifacts/managed/browser-hours-forecast-EtYSBN`.
- `stationQuoteSizingSmoke.mjs`: all 16 width/theme/text cases passed with
  production popup code, staged CSS and synthetic quotes. Output:
  `artifacts/managed/browser-station-popup-e5zQch`.
- Earlier browser attempts exposed the fixed-width label assertion, the old
  stop-card Close action, and an actual three-pixel telemetry-icon overrun at
  320px/200% text. Tests now follow the requested label and Back behavior while
  retaining row/group geometry checks. Narrow telemetry padding fixes the icon
  overrun; the same containment assertions pass without shrinking icons.
- The strict Debug Client build passed. Localhost was restarted on port 5067.
  The user's existing tab was not automatically reloaded.
- No deployment or migration occurred. Live Google/GPU interaction, production
  telemetry and an isolated PostgreSQL execution suite were not run. These
  results do not establish production performance or complete visual correctness.
