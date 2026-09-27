# Dispatch always-open summary review

The owner requested all truck-header information to remain visible on the
Dispatch board. `DispatchPlanning` owns the presentation; the shared
`TruckReadings`, `DriverHours`, `DriverDutySummary` and `DriverNextRecap`
continue to render the existing values. No calculation, request, cache,
retained-result boundary or business formula changes.

Removed the header disclosure, its state and unused styles. The duty and
recap row remains open. Fleet Map disclosures are unchanged. Updated the
component regression and browser assertions for the new behavior, preserving
request-count, freshness and geometry checks.

Validation against the patch on `8c6c506f`:

- `bash test.sh dispatch styles`: Client 707, Server 1650, JavaScript
  styles 139, Dispatch 16, architecture 67; no failures.
- Pinned CSharpier check and changed JavaScript/SCSS Prettier formatting.
- Client Release publish `scratch-rtLp1e` succeeded. An earlier diagnostic
  publish failed because of incompatible Wasm flags; corrected the command.
- Offline UI smoke: `browser-ui-u53cez` at 1440/light/100 and
  `browser-ui-n5FI9I` at 390/light/200, both passed. Inspected actual Dispatch
  screenshots. Reports and affected-test log are pinned in those runs.
- PostgreSQL execution and live performance not tested; no persistence change.
- Runtime auditor detection is inapplicable to this presentation-only change.
  Existing persisted rows are untouched; component and browser regressions
  cover the visibility invariant.

Truck 11005 missing planning/fuel data is a separate investigation. Removing
the disclosure does not establish that incident as resolved. No deployment
has been performed for this patch.
