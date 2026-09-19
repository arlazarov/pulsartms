# Truck inspector disclosure and route summary

Local UI follow-up, not deployed by this change.

- Both truck densities retain the 64rem inspector cap and the same primary
  identity, telemetry, HOS, actions and GPS layout. HOS uses the shared
  `control-touch` diameter. Details adds trailer, duty/recap and the dispatch link
  below the primary summary; disclosure does not animate.
- The route summary groups the load reference, distances, next address and
  appointment, and ETA. Miles and kilometers are visible in both densities.
- The header no longer repeats next-stop mileage. When the tracked next stop is
  the final route stop, Remaining replaces the duplicate next-stop distance.
  Equal rounded mileage alone does not establish that identity.
- The extra final delivery appointment is suppressed only if the next stop is
  that final delivery and its formatted appointment matches. Missing or differing
  source appointment data is not silently discarded.

## Verification

- `bash test.sh fleet styles` passed during iteration, including dependent
  categories and architecture checks.
- Final `bash verify-release.sh`: 456 Node, 1,593 Server and 729 Client tests
  passed, with strict builds and integrity checks for 264 published assets.
- Staged local artifact: `artifacts/managed/release-jOzzVO/publish/wwwroot`.
- Browser verification uses synthetic provider data, installed Chrome and the
  staged artifact, not authenticated production APIs. Regression assertions cover
  stable primary-summary geometry on disclosure, equal HOS diameters, visible
  kilometers and suppression of final-stop duplicates.
- Final browser matrix passed all ten light/dark cases at five widths, including
  enlarged-text checks, with no browser errors or unexpected network requests.
  Evidence: `artifacts/managed/browser-hours-forecast-zlrtWz`. Desktop and phone
  screenshots were inspected.
- No server implementation, database schema or migration changed in this work.
  PostgreSQL fixture and authenticated live-provider checks were not run.
