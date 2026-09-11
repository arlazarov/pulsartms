# Unified map inspector and compact workspace follow-up

## Scope

This continues the [paused checkpoint](unified-map-inspector-pause-2026-09-10.md)
after the user asked to resume. Later feedback superseded the original centered
Dispatch layout: Fleet and Dispatch information groups must be compact and
left-aligned. The final request limits the Fleet inspector's overall width and
centers that panel horizontally while keeping its contents left-aligned.

- Fleet uses one flush top inspector for the truck, current stop, future stop or
  fuel station. It is width-bounded and horizontally centered at the map's top
  edge, without resizing the map or covering the page heading and filters.
  Back and Close preserve the selected truck route.
- The existing stop and fuel renderers share a persistent native host. Ownership
  and revision guards prevent stale updates from reclaiming the visible panel.
  Explicit keyboard dismissal restores focus without scrolling the map.
- Truck readings, HOS, duty status and actions are adjacent on the left. The
  route summary similarly packs distances, load, address, appointment and ETA.
  The header illustration faces right; map heading markers are not mirrored.
- Fuel markers have a 16px visible diameter, with selection/editing indicators
  and separate numbered fuel labels retained. Pointer and touch selection retain
  their independent hit targets.
- Dispatch keeps one heading, toolbar and loading/error shell across Cards,
  Table and Papers. Its truck header is left-packed rather than stretched.
- The fuel editor has a full-height route timeline beside station controls on
  desktop, without a second map. Narrow screens retain separately scrollable
  timeline and controls, a fixed footer and accessible touch targets. Dragging,
  quantity steps, Full tank and existing server-provided costs remain supported.

SCSS owns layout through named tokens. No server financial formula, database
schema or provider contract was changed by this follow-up.

## Verification record

The final source is staged at
`artifacts/centered-map-verified.dnggi4/publish/wwwroot` and served by the local
Client at port 5067. Earlier `RfcRZ8` and `jggL2S` artifacts do not include the
final centered width cap and large-text wrapping fixes. `VdghKP` contains the cap
but precedes the final text-responsive mobile HOS dial sizing.

- Strict Release Client publish passed with warnings treated as errors and
  shared compilation disabled. Static-asset verification passed for 249 assets
  and seven JavaScript entry-point dependency graphs.
- `bash test.sh all` passed: 1,283 Server, 596 Client and 376 JavaScript tests
  (2,255 total), including architecture checks. Logs are retained under the
  same artifact directory in `publish.log` and `tests-all.log`.
- Staged UI smoke passed 44 page/theme/text-size scenarios. Held initial reads
  and view switching retain the Dispatch heading, toolbar and body geometry.
- Future-stop inspection passed seven scenarios, including 2344px light/dark
  checks that exercise the width cap, plus the original desktop/mobile cases.
- Fuel-editor smoke passed four scenarios, including mouse/touch ordering,
  Full tank, server-provided costs, simulated save/cancel and map access.
- Native inspector smoke passed eight scenarios: both themes, desktop/mobile,
  and 100%/200% root text size. It checks the production renderers/controller,
  centered cap, map hits through both exposed sides, ownership, focus and
  horizontal bounds in its representative shell.
- The final HOS/ETA browser matrix passed ten scenarios. Actual map geometry and
  host identity remain unchanged through selection, cold reads, polling, future
  inspection and closing. The 200% text checks assert non-overlapping groups and
  time text inside the enlarged HOS dials, not merely absence of parent overflow.
  At 390px, mobile dials retain 52px at ordinary text size and become 104px at
  200%; the four circles wrap cleanly without shrinking time text.

Browser reports and screenshots are local under `Client/test-results/` in
`centered-map-verified-ui`, `centered-map-verified-stop-details`,
`centered-map-verified-fuel-editor`, `centered-map-verified-native` and
`centered-map-verified-hours`. The completed reports above contain no unexpected
requests or browser errors. Their fixtures and limitations are documented in the
[browser guide](../../../Client/tests/browser/README.md).

The marker GPU probe passed at DPR 1 and 2 earlier in the follow-up; its source
was unchanged by the subsequent layout-only work. That separate evidence remains
under `Client/test-results/unified-inspector-reviewed-gpu` and is not a final
Blazor or Google Maps integration run.

Manual verification of the final live Client at a 2344px viewport measured a
2097px map and 1344px inspector, with equal 376.5px side gaps and no top gap.
The truck illustration faces right, data remains left-packed, and the viewport
override was reset after verification. The 200% checks use root text sizing,
not the browser's native zoom control.

## Limitations

- Deterministic browser fixtures intercept APIs and/or map ports as stated in
  each report. They do not validate live routing, ETA or fuel calculations.
- Manual checks used the real localhost application and Google map for truck,
  stop and fuel inspection, returning to the truck and opening the fuel editor.
  Editing checks were cancelled without saving business data.
- No isolated PostgreSQL fixture was available; PostgreSQL integration checks
  were not run. No local SQL server or database container was started, and the
  application database was not used as a disposable test fixture.
- No migrations were introduced or applied. No production deployment occurred.
  Performance was not measured; test results do not establish complete visual
  correctness of unrelated application screens.
