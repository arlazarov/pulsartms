# Mobile truck inspector layout

Local-only implementation. No deployment or database changes.

## Changes

- Hide the separate Fleet Map page heading below the mobile breakpoint.
- Share one wrapping row for driver and trailer, omitting the Driver label.
- Place vertical Speed, Fuel and Engine readings beside two-by-two HOS clocks.
- Preserve shared text, icon and clock sizes and both disclosures.
- Stack groups below the content threshold, including enlarged root text.
- Reserve duty text space so cold HOS loading does not shift route details.

Shared HOS and metric-fuel components expose layout properties; desktop defaults
remain unchanged. Page styles do not own HOS internals.

## Verification

- Client Debug build: no warnings or errors.
- Final `bash test.sh all`: 821 Client, 1,694 Server and 516 Node tests passed.
- An earlier full run hit a DispatchBatchRefresh bUnit timeout; the unmodified
  test passed on the final full rerun. No timeout or assertion was weakened.
- Corrected a style-test selector that matched a scoped Settings width rule
  instead of the standalone card surface rule.
- The Fleet browser fixture uses the compiled application and offline provider
  data, not a hand-built HTML reproduction. It checks mobile widths 320–767,
  100%/200% root text, both themes, cold loading and disclosure stability.
- Updated browser fixtures to supply account units through Appearance, matching
  the existing personal-settings contract rather than company settings.
- Final Fleet browser smoke: 12 width/theme cases passed, with no browser errors
  or unexpected network requests. Pinned evidence is in
  `artifacts/managed/browser-hours-forecast-BSQVss`.

The separate visible browser requires localhost sign-in, so screenshots use
explicit test data. Live provider behavior, PostgreSQL execution and the complete
cross-page UI smoke were not run for this presentation-only change.

## Follow-up: compact rows and temperature

- Restore four HOS clocks to one content-sized row, with 40px clocks and 8px gaps.
- Keep mobile telemetry content-sized with 24px icons and shared body text.
- Replace the visible outside-temperature label with an accessible thermometer
  icon. Temperature joins Speed, Fuel and Engine on mobile.
- Personal temperature choices are Celsius and Fahrenheit only; distance choices
  are unchanged. Legacy Both values display Fahrenheit without an automatic
  account update. The API retains compatibility with existing saved values.
- Preserve the captured card height when opening Location & load details.
- Full suite: 822 Client, 1,694 Server and 518 Node tests passed.
- Fleet browser smoke: 12 width/theme cases passed without browser errors or
  unexpected requests. Evidence: `artifacts/managed/browser-hours-forecast-Z1cPjQ`.
- Personal-settings browser smoke: two viewport cases passed, including saved
  unit restoration in a new synthetic device context. Evidence:
  `artifacts/managed/browser-ui-fKn9ZO`.

Browser checks use the compiled application with deterministic API fixtures, not
real account writes. No deployment, migration or database changes were made.

## Follow-up: desktop temperature placement

Desktop temperature now occupies a fourth content-sized telemetry column beside
Engine, with its icon above the value and a shared subtle divider. It no longer
adds a separate row. Mobile presentation is unchanged by this follow-up.
The style regression now requires four columns; the browser regression requires
temperature to remain beside Engine in both desktop disclosure states and mobile.
An enlarged-text narrow desktop case exposed overflow; the content-width fallback
now wraps readings only when necessary. Style compilation and `bash test.sh styles`
passed, including architecture checks. The final Fleet fixture passed all 12
width/theme cases with no browser errors or unexpected requests; evidence is in
`artifacts/managed/browser-hours-forecast-iwrSFE`. The full suite and live provider
checks were not rerun for this style-only follow-up. Localhost serves the updated
stylesheet; nothing was deployed.

## Follow-up: Celsius default and revised phone columns

- Celsius is the fallback for new, absent and legacy Both preferences. Explicit
  Fahrenheit remains selectable and persists through the existing account API.
- Mobile telemetry forms a compact left column; four HOS clocks share one row
  on the right, with duty immediately below and a subtle vertical divider.
  Narrow or enlarged-text layouts stack groups without clipping readings.
- Mobile expanded Remaining uses an inline wrapping row; load metadata uses
  tighter token spacing. The captured disclosure height remains unchanged.
- The proposed address and load-link relocation was cancelled. Their original
  positions remain. Only the GPS date/time row was removed, as requested.
- Full automated suite passed: 822 Client, 1,694 Server and 519 Node tests.
  Client Debug build completed without warnings or errors. Styles compiled.
- Personal-settings browser checks passed at desktop and mobile widths,
  including Celsius fallback and explicit Fahrenheit restoration on a new
  synthetic device. Evidence: `artifacts/managed/browser-ui-vIgeDH`.

These checks do not use real account writes, live providers or PostgreSQL.
No deployment or migration was performed.

## Follow-up: one disclosure and full-width phone location

- Keep Speed/Fuel above Engine/Temp in the left two-by-two group; four HOS
  clocks stay together on the right, with duty beneath. Stable telemetry tracks
  prevent delayed temperature/engine values from moving the HOS group.
- Add the visible Temp label. Keep the desktop address/link position unchanged
  and the GPS timestamp removed.
- Place the phone address/link across the full width below telemetry and HOS.
- Remove the secondary Location & load details button and its state. The main
  Details/Hide controls all retained mobile content without extra reads.
- Size the phone card to its header and summary, ending at the load link.
  Route facts extend its scroll range rather than increasing natural height.
  The map bounds short/enlarged-text layouts. No camera listener is added.
- Group Load/order/total beside Remaining, followed by address and arrival
  facts. Fix the compact stacked selector overriding mobile grid placement.
- Final full suite passed: 822 Client, 1,694 Server and 520 Node tests.
  Earlier concurrent runs had an allocation assertion and a Dispatch timeout;
  the final run passed unchanged server/Dispatch tests. These were not treated
  as fixes to those unrelated tests or as production performance evidence.
- Fleet browser fixtures passed all 12 width/theme cases, including cold
  loading, 320–767px layouts, 200% text, scrolling and unchanged camera bounds.
  No browser errors or unexpected requests. Inspected top and scrolled phone
  screenshots; pinned evidence: `artifacts/managed/browser-hours-forecast-sFuC4s`.

Browser evidence uses the compiled application and synthetic API/map data.
Live providers, real PostgreSQL execution and production deployment were not run.
