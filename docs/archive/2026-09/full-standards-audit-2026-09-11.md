# Standards audit and conservative cleanup — September 11, 2026

## Scope and outcome

The request began as an audit of Client, Server and especially SCSS, including
leftovers from recent iterations. The subsequent request authorized removing
unneeded files. Cleanup removed confirmed unused implementations and generated
local outputs, not business features or the active regression suite.

This was repository-wide inventory/reference scanning, the full automated suite,
isolated Client publishing, selected offline browser scenarios and focused manual
review of changed-risk areas. It was not a line-by-line certification of every
file, a penetration test or a production performance measurement. There is no
Git repository in this working directory, so no trustworthy historical diff was
available. Source backups were made before destructive cleanup.

No production deployment, database mutation, migration, credential change or
saved fuel-plan recalculation was performed. Existing localhost processes on
5067 and 5086 were retained.

## Remaining findings

### Confirmed preference — technical Calculate Fuel details stay in the browser console

[FuelRecalculateButton.razor.cs](../../../Client/Shared/FuelRecalculateButton.razor.cs),
lines 42–53, logs server failure reasons and invokes a parameterless failure
callback. Its Razor view contains only the button.
[FleetMap.razor.cs](../../../Client/Pages/FleetMap/FleetMap.razor.cs),
`OnFuelFailed`, only schedules route revalidation. A failed explicit calculation
can therefore complete without displaying the technical reason in the page.

After reviewing this observation, the user explicitly requested keeping technical
details in the console, clarifying that they meant the browser console rather
than the server terminal. The existing Client logger already follows that choice.
No detailed error popup was added, and this is not classified as a defect against
the user's chosen presentation policy. No server logging change was made.

### P2 — An optional browser scenario still encodes superseded UI requirements

[hoursForecastSmoke.mjs](../../../Client/tests/browser/hoursForecastSmoke.mjs),
lines 634–641, requires a zero top inset, no shadow and zero inspector radius.
Those assertions conflict with the current shared UI guide and the requested
floating inspector. This scenario was inspected but not executed in this audit;
it must not be described as passing. Retain its valuable ETA/HOS lifecycle checks
and update only the outdated presentation expectations in a separate follow-up.

The independent native-inspector scenario had analogous stale assumptions. Its
12-hour clock assertions, planned-fuel width token and coordinated top/side inset
expectations were updated. It now mounts the real camera-viewport observer and
passed all eight cases; no assertions were simply disabled.

### P3 — HOS sizing bypasses the shared composition contract

[styles.md](../../architecture/styles.md) asks consumers to configure HOS through
custom properties rather than override internal selectors. However,
[_driver-status.scss](../../../Client/Styles/components/_driver-status.scss),
line 26, replaces the custom-property dimensions with fixed 52px mobile dimensions;
[_truck-info.scss](../../../Client/Styles/pages/fleet-map/_truck-info.scss),
line 129, directly overrides the dial again. Font sizing still reads
`--hos-dial-size`, so the dimension and type calculations have separate sources.

Follow-up: consolidate diameter and type scaling into the component contract and
verify Dispatch and Fleet Map at narrow widths and enlarged text. This is a
confirmed ownership inconsistency, not a claim that every current dial clips.
No visual redesign was made during cleanup.

## Confirmed unused source removed

| Family | Removed | Retained deliberately |
| --- | --- | --- |
| Old fuel summary | `Client/Shared/FuelPlanSummary.razor`, its code-behind, `_fuel-plan-summary.scss`, the SCSS index import and `FuelPlanSummaryTests.cs` | Current inspector/editor fuel presentation and the browser assertion that the old summary must not return |
| Old checked-itinerary reuse entry point | `FuelPlanReuse.cs`, `ReusableFuelItinerary` and `FuelPlanReuseTests.cs` | Active `FuelHorizon`, projection/storage compatibility and legacy saved-snapshot readers |
| Unused contracts | `ExternalDailyHosLog`, `IdentityLoginResult`, `SamsaraDailyHosLog`, `SamsaraHosLogMetaData`, `SamsaraHosVehicle` | `SamsaraDriverReference`, still used by HOS history, moved into its own correctly named file |
| Unused selectors | `fleet-map-info-reserved__close`, `fleet-map-mobile-summary__empty`, `fleet-map-mobile-summary__clear`, `dispatch-page__table-wrap`, `dispatch-board__eyebrow`, `dispatch-board__subtitle`, `dispatch-planning__station` | Current inspector controls, page layout, dynamic map classes and framework validation classes |

The two removed test classes contributed 41 test cases: 12 Client component cases
and 29 server algorithm cases. Their production entry points had no live callers;
the tests only exercised those obsolete implementations directly. No architecture
checks were weakened or excluded.

The SCSS graph decreased from 60 to 59 files. In-memory expanded Sass output
decreased from 205,521 to 200,157 bytes: 5,364 bytes, approximately 2.6%.
Distinct compiled class tokens decreased from 541 to 526. These are uncompressed
stylesheet measurements, not browser transfer or execution-time measurements.

Literal-reference scanning was only a candidate finder. Dynamic stop-label and
station-price classes, moving/idling modifiers, framework-generated validation
classes, reflection-discovered handlers and DI registrations were not deleted
merely because a literal consumer was absent. Standalone details-card fallback
implementations still have imports/default factory consumers and were retained.

## Generated outputs removed and recovery

608 generated-output entries were moved out of the workspace, including old
isolated builds, publish staging copies, diagnostics, browser screenshots and old
test-project `bin`/`obj` directories. An entry can be a whole directory.
Their measured allocated size was **22.6408 GiB**. The `artifacts` directory was
approximately 21 GiB before cleanup and 972 MiB after fresh verification outputs
were produced. Old `Client/test-results` alone occupied approximately 1.7 GiB.

Recovery location:

`/Users/antonarlazarov/.Trash/AMFTMS-cleanup-2026-09-11-ItvH3I`

`generated/` preserves original relative paths. `source-before-cleanup/` contains
14 original source files, including the deleted components/tests and edited SCSS;
the two native-inspector browser files have additional before-copies at the trash
folder root. The exact generated-file manifest is also stored there and at
`artifacts/full-audit-2026-09-11/cleanup-manifest.json`.

Moving to Trash reduces workspace size but does **not** release disk capacity
until that trash content is permanently removed. The Trash was not emptied.

Retained outputs include:

- `artifacts/client-account-time-final`: active local Client runtime.
- `artifacts/truck11007.HTCy9n/api-optional-stop`: active local API runtime.
- `artifacts/release.QcB6c0`: latest recorded Client publication staging.
- `artifacts/fuel11007-readonly.pcNrKd`: recent server-release/diagnostic evidence.
- This audit's reports/builds and one fresh `artifacts/tests` build cache.

Running-process open files were checked before moving generated targets.
Credentials, migrations, application data, dependency manifests, active test
sources and historical documentation were retained. `.dockerignore` already
excludes `artifacts`, test-results, ordinary build outputs and diagnostic heaps:
removing those local files does not make the running server faster or remove
22 GiB from its deployed image.

## Documentation cleanup

The Fleet Map ownership guide and browser README now describe coordinated
top/side insets and the mobile editor panes instead of obsolete flush-top and
visible-map-strip requirements. Historical audit documents remain historical;
old inventory links to removed source files are not proof those files still exist.

## Verification

| Check | Result |
| --- | --- |
| Full suite before cleanup | 1,458 Server + 647 Client C# + 399 JavaScript = 2,504 passed |
| Final `bash test.sh all` | 1,429 Server + 635 Client C# + 399 JavaScript = 2,463 passed; architecture categories included |
| Client JS type check | Passed before cleanup; production JavaScript was not modified |
| `npm run styles:build --prefix Client` | Passed; regenerated CSS/version stamp |
| Isolated Release Client publish with warnings treated as errors | Passed after source cleanup |
| Offline staged UI smoke | 44 page checks across 12 viewport/theme/root-font cases; no failures or unexpected requests |
| Actual staged Fuel editor | Eight cases passed, including mobile panes, ordering and fixture-only save/preview interactions |
| Native inspector integration | Eight cases passed using actual production renderers/viewport observer and staged CSS |
| Local runtime retention | Same Client/API listeners remained on ports 5067/5086 |

The native-inspector probe was first invoked from the wrong working directory;
after correcting that invocation it exposed obsolete assertions, which were
updated as described above. Only the successful final run is counted as passing.
Release publishing reported that the optional `wasm-tools` optimization workload
is not installed; no optimized-WASM size or runtime claim is made.

Logs and JSON/screenshot evidence are under `artifacts/full-audit-2026-09-11*`.
All browser traffic used local fixtures; simulated saves did not write business
records. Representative screenshots were inspected, not every possible screen.

Not run: real PostgreSQL execution/migrations (no safe isolated fixture), live
provider integrations, production authentication/security rollout, load testing,
GPU map validation, real-device zoom and the remaining optional browser scenarios.
No migrations were created or applied. Full automated success is not certification
of production performance or complete visual correctness.
