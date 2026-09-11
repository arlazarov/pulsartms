# Component style ownership — September 11, 2026

## Changes

- Grouped fuel editor, reading and recalculation styles under
  `Client/Styles/components/fuel/`, with one `_index.scss` entry point.
- Split driver status into hours, duty, arrival and stop-hour partials under
  `Client/Styles/components/driver-status/`. Removed the superseded flat files
  and updated imports and direct style-test consumers.
- Moved HOS internal geometry, wrapping, label and value typography into its
  component. Fleet Map and Dispatch now configure their wrappers through CSS
  properties rather than overriding internal HOS selectors. Shared dial tokens
  provide normal and compact defaults; HOS value text is bounded by the effective
  dial diameter without applying that typography rule to fuel gauges.
- Added architecture checks for page/component ownership and single module
  emission. Extended browser probes for responsive HOS geometry and custom sizing.

This resolves the HOS sizing finding in the
[earlier standards audit](full-standards-audit-2026-09-11.md), not every remaining
finding or every possible component ownership issue.

## Verification

- `bash test.sh all`: 1,429 server, 635 Client C# and 412 JavaScript tests passed
  (2,476 total; none skipped by the runner).
- Styles compiled and a strict isolated Release Client publish completed.
- Staged `uiSmoke.mjs`: 44 page checks across light/dark themes and 100%/200%
  root font sizes passed, including Dispatch HOS ring/text geometry.
- Staged `fuelEditorSmoke.mjs`: all eight desktop/mobile scenarios passed.
  Additional selected-truck HOS probes cover 320, 390, 768, 1200, 1440 and 2000px
  at both font scales and themes, including `60:59` and a custom 72px diameter.
  Dynamic sizing assertions wait for layout to settle after changing the wrapper
  parameter; immediate reads during responsive layout previously returned the
  old dimensions despite the updated computed custom property.
- Inspected the 320px light-theme editor screenshot: route controls, stop rows
  and persistent save footer remain visible.

Evidence is disposable under `artifacts/managed`: `diagnostic-W580pA`,
`scratch-hivlXY`, `browser-ui-I1uxyf` and `browser-fuel-editor-f1wPbi`.
Browser checks used intercepted fixtures, not live providers, real authentication
or database writes. PostgreSQL checks and the separate `hoursForecastSmoke.mjs`
lifecycle suite were not run. No migrations, deployment or localhost restart
were performed. These results do not establish complete visual correctness.
