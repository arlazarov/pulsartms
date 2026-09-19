# Compact truck telemetry

The compact Fleet Map inspector now retains the existing Speed, Fuel and Engine
readings instead of hiding Speed and Engine. This is Client SCSS composition only;
it reuses the same telemetry, components and refresh without API changes.
Below the existing 40rem inspector threshold, the readings occupy an aligned
three-column row above the actions. Desktop height remains unchanged. The mobile
fixture grows by approximately 26 CSS pixels; its content-height limit is now
360px instead of 330px to accommodate the added row without shrinking typography.

## Verification

- `bash test.sh fleet styles` passed: Server 290, Client 209, plus map, styles and
  architecture Node suites. Added a compact telemetry visibility/style regression.
- Strict isolated Release Client publish succeeded at
  `artifacts/managed/scratch-vDQMnJ/publish`; 264 asset integrity checks passed.
- Fleet-only `hoursForecastSmoke.mjs` passed all ten light/dark scenarios at
  2344, 1920, 1440, 1200 and 390px, with no browser errors or unexpected requests.
  Checks include provider values, aligned visible readings and existing inspector
  interactions. Evidence: `artifacts/managed/browser-hours-forecast-2cCxNR`.
- Desktop and mobile screenshots were visually inspected from the first browser
  run. That run only failed the previous mobile height limit; the updated run
  passed with the same application artifact.
- `git diff --check` passed. Full suite, authenticated provider checks and
  PostgreSQL checks were not run. No server, database or deployment changes.
