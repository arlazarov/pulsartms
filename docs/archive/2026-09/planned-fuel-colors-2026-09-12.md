# Planned fuel colors — 2026-09-12

The station controller now retains price colors regardless of ordinary station
visibility. Cold planned stops compare available server-supplied prices within
each currency without loading the catalog. A single price tier uses the existing
middle color; missing cash or IFTA prices remain neutral. Selection and editing
retain the same palette. This is Client display logic, not a financial calculation
or a change to saved fuel plans.

## Checks

- `bash test.sh map`: passed, including dependent Fleet and architecture checks.
- `npm run js:check --prefix Client` and `npm run js:build --prefix Client`: passed.
- `fuelMarkerVisibilitySmoke.mjs`: four cases passed with no browser errors or
  unexpected requests. Light-theme GPU screenshot inspected; the planned marker
  remains amber with ordinary stations hidden, and its badge opens the popup.
- Evidence: `artifacts/managed/browser-stop-cards-uxTTFt/report.json`.
- The broader `stopCardsSmoke.mjs` stopped at its existing expectation that
  unselected routes are 5px wide, before reaching fuel assertions. That unrelated
  route assertion was not changed. This is not a full browser-suite pass.
- `git diff --check` passed. Full release gate, live provider and PostgreSQL checks
  were not run for this change. No migrations or deployment.
