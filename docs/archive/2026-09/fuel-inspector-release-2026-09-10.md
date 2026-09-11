# Fuel inspector and terminal-value release — September 10, 2026

## Scope

- Compact planned-fuel and current/future stop inspectors, comparison-column dividers,
  and shared arrival-fuel gauges showing server-provided percent and US gallons.
- Planned fuel markers remain neutral with Fuel Stations disabled. Opening a fuel
  inspector still loads the selected day's quotes without enabling the marker layer.
- Fuel selection version 23 values useful post-delivery fuel up to the fill limit
  even in well-served regions. The hard arrival minimum is unchanged. Regression
  cases cover cheaper, more expensive and equal-price replacement fuel.
- Existing earlier local fuel, telemetry, quote comparison and Dispatch changes
  are included in the same workspace release.
- Version 24 additionally removes per-leg whole-gallon consumption rounding from
  selection cost and balances. Automatic quantities are 25 gallons minimum, then
  30/40/50-gallon steps or the exact full target. Saved/manual plans are not rewritten.
  Continuous labels remain bounded by fuel bands; this is not an exhaustive optimizer.
- The subsequent inspector adjustment adds the shared `md` top gap (12px at the
  standard root size), small radius and subtle card shadow. It remains an absolute
  non-modal overlay; the map and the internal row separation are unchanged.
- After the initial deployment, the user explicitly removed the fixed purchase-stop
  charge. Version 25 sets the effective fleet charge to zero, including legacy
  stored overrides, while preserving driving-time/access-fuel costs, the 25-gallon
  automatic minimum and ten-gallon purchase steps. Settings reads do not rewrite
  stored JSON; affected fuel signatures require an explicit recalculation.

## Verification

- The pre-rounding-fix `bash verify-release.sh`: 1,417 server, 636 Client C#, 393 JavaScript tests passed;
  strict Release solution build passed. The artifact verifier checked 249 assets
  and seven generated JavaScript dependency graphs.
- Pre-rounding-fix staged Client: `artifacts/release.RK6HL1/publish`.
- Offline UI smoke on that artifact passed 12 viewport/theme/font-size
  configurations with no failures, unexpected requests or browser errors.
- The first Cloud Build stopped before deployment because Linux rejected those
  incorrectly cased test imports. The release gate was not bypassed.
- The second Cloud Build stopped because `StampStyleVersion` invoked Node in the
  .NET-only phase. It now honors `SkipStyleBuild`, while the preceding Node phase
  still stamps CSS. A regression verifies both sides of that split-toolchain contract.
- The third Cloud Build passed all 1,417 server tests but timed out in one
  next-load-selection Client test. Ten consecutive local Release runs of that
  exact test passed. No test was skipped, weakened or given a larger timeout;
  the complete cloud gate was retried. This does not establish the cause of the
  intermittent timeout or claim it has been fixed.
- The fourth Cloud Build (`c6578da8-d1cf-4bf4-b941-9ad3c88c9a55`) passed, but the
  deployment coordinator was stopped before Cloud Run rollout when the 54777
  selection defect was confirmed. That image does not contain version 24.
- Version 24 iteration: `bash test.sh fuel` passed 934 server and 184 Client C#
  category/dependency checks plus 19 JavaScript architecture checks. New regressions
  cover the 54777 station pair, purchase thresholds, exact capacity, and fractional
  access across a full 24-station shortlist on 500- and 2,000-mile synthetic routes.
- Final version-24 `AMFTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome bash verify-release.sh`
  passed 1,430 server, 636 Client C# and 393 JavaScript tests (2,459 total), strict
  builds, 249 static assets and seven generated-module graphs. The final Client
  artifact before the final inspector gap is `artifacts/release.11ApWd/publish`; all 12 offline UI configurations
  passed again. Authenticated live-browser release mode was not run for this final gate.
- After the inspector gap, `bash test.sh styles` and the complete release/UI gate
  passed again with the same 2,459 tests. Final hosted Client stage:
  `artifacts/release.o13ysu/publish`. Six compiled-CSS fixture checks at
  390/1440/2344px and 100%/200% root size confirmed the proportional top gap,
  centered/capped panel, radius/shadow and unchanged map bounds. These use fixture
  markup, not authenticated live map rendering.
- Version 25 without the fixed fee passed `bash test.sh fuel` (935 server,
  184 Client C#, 19 JS architecture checks), then the full strict release gate:
  1,431 server, 636 Client C# and 393 JavaScript tests (2,460 total). All 12
  offline UI configurations passed. Final zero-fee Client stage:
  `artifacts/release.XD93xk/publish`.
- Live local inspection confirmed the compact Delivery card and arrival-fuel gauge.
  These checks do not establish complete production map/GPU or responsive correctness.
- No schema migration was added. No isolated PostgreSQL execution fixture was
  available; PostgreSQL-specific tests and production performance measurements
  were not run. No saved fuel plan was recalculated as part of deployment.

## Diagnostic limitation

The reported missing 11007 arrival values were diagnosed separately: the saved
fuel plan was calculated at 19:05 Toronto time, while the current route had been
rebuilt at 19:49. Read-only comparison put GPS approximately 4.97 miles from the
saved fuel baseline, beyond its two-mile validation allowance. Invalid forecasts
remain unavailable; this release does not claim to repair arbitrary stale fuel
geometry automatically.

The saved 54777 calculation selected LOVES #366 at $5.924/gal over LOVES #682 at
$5.745/gal. Both had the same estimated one-mile access and four-minute time cost.
Per-leg consumption rounding assigned 18 gallons to #366 versus 19 to #682 and
the same 125-gallon arrival, creating an artificial $2.523 advantage. Version 24's
replay keeps equal actual consumption; both buy 25 gallons under the new minimum,
and #682 costs $4.475 less. This comparison uses the captured profile and price date,
not a claim that those quotes or GPS remain current.

A separate read-only 11007 comparison used the saved version-24 calculation at
01:07 UTC September 11 (September 10 Toronto time): 140 starting gallons,
6.720416657 MPG, #435 at $5.604 versus #790 at $5.543. Going directly to #790 was
unreachable. A 30-gallon bridge at #435 followed by a full #790 purchase was feasible:
27.046 gallons at #790, 222.954 gallons bought there and 176.892 at delivery.
The one-stop plan retained 120.204 at delivery. Including terminal replacement
fuel at $5.696, the scores were $1,860.695 for one stop and $1,865.047 for the
two-stop alternative. The approximately $4.35 difference includes a $20 extra
stop and estimated access time; it does not assume the extra purchase coincides
with an already-required driver break. The diagnostic did not save or change a plan.
After the user's explicit removal of the fixed charge, the same captured inputs
favor the 30-gallon bridge followed by #790's full fill by approximately $15.65.
A regression checks that choice, the exact zero-fee/access-time cost, capacity and
reserve. Integration regressions verify legacy settings cannot restore the fee
and that changing fuel economics does not change the mandatory road input hash.

A later read-only 11007 snapshot (01:22 UTC September 11) already used version 25
and selected three purchases: 25 gallons at #435 ($5.604), a full fill at #371
($5.523), then 40.737 gallons to full at #405 ($5.579). The last purchase is not
needed for the remaining distance: removing it and its estimated one-mile access
leaves 164.704 gallons (65.88%) at delivery, above the 37-gallon arrival minimum,
versus 205.292 gallons with it. Its terminal replacement valuation at $5.696 yields
only about $1.585 net estimated savings after access fuel and four minutes at $35/h.
The existing economic-only policy deliberately permitted this top-up above 80%; it
was not a missing-fuel projection. The user approved avoiding the dearer optional
stop when the destination area is reasonable and onward access is covered.
Version 26 therefore excludes a dearer subsequent purchase when filling at the
selected cheaper station can cover the entire calculated horizon and the arrival
minimum. This requires a non-poor area and an identified exit whose estimated
fuel plus reserve fits that minimum. Capacity, not the selected purchase volume,
is tested so a partial fill cannot evade the restriction. Poor/unknown areas,
necessary range purchases and later cheaper stations retain their evaluation.
No fixed fee was reinstated and no stored plan was changed by the diagnostic.
Six unit regressions cover the captured two-stop result and those exceptions.
The affected dependency gate passed 941 server, 184 Client C# and 19 JavaScript
architecture checks. The full version-26 release gate passed 1,437 server, 636
Client C# and 393 JavaScript tests (2,466 total), strict builds, 249 staged assets,
seven JavaScript dependency graphs and 12 offline UI configurations. Stage:
`artifacts/release.0vkZmo/publish`. The local API was restarted on version 26 and
its liveness endpoint returned Healthy. Authenticated live-browser and isolated
PostgreSQL checks were not run; estimated exit access is not a checked road.

## Deployment evidence

- Cloud Build `1299bcc0-378c-480b-babb-a4e1e6b55ebe` passed the complete cloud gate
  (1,430 server, 636 Client C#, 393 JavaScript tests) and built version 24.
- API image digest: `sha256:f924ef3767f7b54676d5ae6f69d6c3ce7fa2406c14d774574f586804fc0a8824`.
- Cloud Run revision `amftms-api-00093-p7g` serves 100% of traffic. Environment,
  database identity and service account fingerprints match the pre-release snapshot.
- The initial Firebase release deployed `artifacts/release.o13ysu/publish` to both
  `amftms.web.app` and `tms.amfcarrier.com`. All 161 post-release checks passed:
  77 JS/CSS/WASM assets plus index/SPA checks on each origin and three liveness
  endpoints. This includes the inspector gap but precedes removal of the fixed fee.
- Version 25 passed the complete cloud gate (1,431 server, 636 Client C# and
  393 JavaScript tests) in build `208acdb5-c4bd-47f2-81bb-1430a0644740`.
  Revision `amftms-api-00094-zqk` serves its image digest
  `sha256:da19198d1ff120d0f2899a1057dc0fc2081cb740f276df4de001b7cfa455801e`.
  Firebase deployed the verified `artifacts/release.XD93xk/publish` stage.
  All 161 post-release checks passed; environment, database identity and service
  account fingerprints matched the pre-release snapshot.

- Version 26 passed Cloud Build `26b40b81-92ab-4bc9-aa47-7a3a47b7ed32` and
  deployed revision `amftms-api-00095-pg7` at 100% traffic, image digest
  `sha256:64621e5e4c6857129ed42b970b1c1cb308449354174f72cf710d7dba636c46c3`.
  Environment, database identity and service account fingerprints remain unchanged.

## Final responsive UI follow-ups

- Phone fuel editing now uses Route, Fuel details and Map views. Route/details
  each receive the full working height inside the existing map. Header and save
  footer remain visible; Map collapses the editor without discarding the draft.
  Selecting a station opens its details. Desktop keeps adjacent scrollable columns.
- Inspector top spacing is capped by measured actual side clearance using the
  existing overlay observer. It is not selected by phone/tablet breakpoints.
  Phone station cards fill the map width but keep content-driven height.
- Current and future stop fuel arrival is independent of station-layer visibility.
  A compact gauge sits in the arrival facts column with its label and gallons to
  the right. Missing/invalid values use a short dash, not a large empty dial.
- Action icons wrap naturally; Fuel metric label/icon/value geometry matches Speed
  and Engine without changing the Dispatch pill.
- Final full gate: 1,437 server, 637 Client C# and 396 JavaScript tests (2,470 total),
  strict builds, 249 assets, seven dependency graphs and 12 offline UI configurations.
  Firebase deployed the exact `artifacts/release.JwiE4u/publish` stage to both hosts;
  all 161 post-deployment asset/index/SPA/liveness checks passed.
- Eight staged Blazor fuel-editor browser cases passed: 1440x1000, 390x844,
  390x667 and 320x667 in light/dark themes, with intercepted fixture API/map calls.
  They exercise real mouse/touch reorder, quantity controls, validation, view
  switching, retained drafts, save/cancel/escape and unchanged map document bounds.
  Native touch can scroll the page slightly; document-coordinate checks distinguish
  that from changing the map layout. These are not live-provider or database tests.
  The desktop cases also check telemetry value baseline/font/line height/icon size.
- Twenty compiled-CSS truck-inspector geometry cases passed at ten widths and
  100%/200% root font sizes after observer/layout stabilization, with one CSS pixel
  tolerance for subpixel layout. They check coordinated top/side spacing, centering,
  bounded width and unchanged map bounds using fixture markup and the real observer.
- Local Client was rebuilt in an isolated directory and restarted on port 5067;
  local API remains version 26 on port 5086. No new migration or stored-plan write
  was made. Existing fuel recommendations need Calculate Fuel to apply version 26.

## Horizontal editor and truck isolation release

- Fuel editing hides other truck markers across all viewport sizes, including
  empty drafts and telemetry refreshes. Closing restores the current Trucks layer
  preference without discarding telemetry. Desktop editing is horizontally
  centered, retaining the bottom inset; mobile editing is unchanged.
- The planned-fuel legend now reads Fuel; actual route markers retain numbering.
- Full release gate passed 1,437 server, 637 Client C# and 398 JavaScript tests
  (2,472 total), strict builds, 249 static assets, seven dependency graphs and
  12 offline UI configurations. Eight staged editor browser scenarios passed
  in light/dark themes, including horizontal centering and the desktop bottom inset.
- Firebase deployed the exact `artifacts/release.kPXQnR/publish` stage to both
  hosting origins. All 161 deployed asset/index/SPA/liveness checks passed.
  Evidence: `artifacts/fuel-editor-horizontal-release-gate.log`,
  `artifacts/fuel-editor-horizontal-release-browser/report.json`,
  `artifacts/fuel-editor-horizontal-deploy.log` and
  `artifacts/fuel-editor-horizontal-deployed-verification.json`.
- API and database were not changed; no migration was required. Authenticated
  live-provider and isolated PostgreSQL checks were not run. Browser scenarios
  use deterministic fixture APIs and map callbacks, not live dispatch writes.

## Local account, clock-format and HOS follow-ups

- Login verifies the existing session through the authentication provider before
  showing credentials. Authenticated visitors navigate to Fleet Map with history
  replacement; stale checks cannot redirect after leaving or disposing Login.
- The account name/role is a keyboard-accessible disclosure for Logout. Existing
  session-bound logout behavior is preserved, with Escape returning trigger focus.
- Appointment windows, ETA/recap alternatives, camera timestamps, HOS update
  tooltips and sync/fuel-check timestamps use zero-padded 12-hour clock times with
  AM/PM. ISO values, timezone offsets and HOS/elapsed durations are unchanged.
- HOS text scales with the circle diameter; the actual Blazor 60:59 value remains
  inside the ring at 1200/1440/2000px and 100%/200% root font sizes in both themes.
  Fuel-gauge typography is unaffected.
- Full verification passed 1,437 server, 647 Client C# and 399 JavaScript tests
  (2,483 total), strict builds, 249 staged assets and seven dependency graphs.
  Twelve offline UI configurations passed, including authenticated Login redirects,
  account disclosure/Escape and longer appointment windows. Eight staged fuel
  editor scenarios passed after restoring viewport geometry between font probes.
- Stage: `artifacts/release.QcB6c0/publish`. Evidence:
  `artifacts/account-time-release-gate2.log` and
  `artifacts/account-time-editor-browser3/report.json`. The isolated local Client
  build is `artifacts/client-account-time-final`; localhost:5067 was restarted.
  Firebase subsequently deployed this exact previously verified stage successfully
  (`artifacts/account-time-deploy.log`). At the user's explicit request, no checks
  were rerun and no post-deployment probes were performed. No API/database/migration changes were
  made; live-provider/authenticated production and isolated PostgreSQL checks were
  not run. Browser data and requests were intercepted fixtures.
