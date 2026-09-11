# Unified map inspector: paused checkpoint

Paused at the user's request on September 10, 2026, before closing the laptop. Do not resume work or deploy until the user asks to continue.

## Requested changes

- Center the Dispatch header's status, fuel and mileage group between the truck identity on the left and HOS on the right.
- Make the map information area a single flush, full-width top panel, without outer gaps or separate floating cards. Keep it positioned over the map so selection does not resize the map or cover the page header.
- Reduce fuel station markers from 20px to 16px while retaining selection indication and usable hit targets.
- Reuse that one panel for truck, route-stop and fuel-station information. Clicking a map object switches content; provide a return to the truck. Remove duplicate lower information cards. Preserve existing ETA, prices, quantities and actions.

## Source checkpoint

- Dispatch three-zone SCSS and focused style tests were edited. Browser geometry assertions were added to `Client/tests/browser/uiSmoke.mjs`; fresh-artifact verification is pending.
- Fleet Map shared inspector Razor/partial and structural SCSS were implemented, including an embedded next-load detail view, a persistent JS-owned native host and retained hidden truck/route content. The combined Client has not been built yet. C#, style and hours-browser test updates were interrupted and must be reviewed before resuming.
- JavaScript docked inspector composition reuses existing stop/station HTML renderers. Its interop contract is `OnMapInspectorChanged(kind, truckId, version)`, `setInspectorMode(kind, truckId)` and `clearMapInspection()`. JavaScript owns monotonic revisions. Ownership guards prevent an inactive popup update or close from overwriting the active view.
- Explicit inspector Close preserves the selected route/truck while hiding the panel. Escape or blank-map interaction returns to the truck when present. Returning to the truck must not invoke the full deselection path.
- Fuel marker source changes and related documentation are in place. Marker-only GPU checks passed at DPR 1 and 2 before the later inspector changes.

## Verification status

These are partial checks, not a completed release: focused Dispatch styles passed 9/9; marker-era map/architecture checks passed 286/286; subsequent inspector map/architecture checks passed 291/291, followed by focused checks at 37/37 after Escape/blank interaction changes. The final combined source still requires verification.

No deployment or new local Client build/restart was performed for this set of changes. The running UI is the previously verified version, not evidence that these new edits work. No database changes are part of this set.

## Resume checklist

1. Review interrupted test edits and the shared inspector implementation before further changes. Preserve all existing work; this workspace is not a Git repository.
2. Read the project UI/testing guides and check the exact JS/C# interop contract and version/selection guards.
3. Check native stop/fuel layout inside the common panel, including planned-fuel controls. Avoid legacy absolute-card classes on the native host.
4. Verify Dispatch header centering, overlap and HOS clipping at narrow widths and 200% text size.
5. Complete relevant regression tests; build the Client and run the full suite because the shared interop contract changed. Do not start a local database server or substitute the application database for an isolated test fixture.
6. Publish a fresh local artifact and verify static-asset integrity. Stop the existing Client dev server before regenerating served output, then restart it. Re-resolve process IDs after laptop sleep; do not reuse recorded process IDs.
7. Run browser checks for truck/current-stop/fuel/future-stop switching, Back, Close, Escape, stale updates and unchanged map geometry. Verify actual reused native HTML and real map interactions, not only stubs. Cancel any editing QA without saving business data.
8. Report remaining checks honestly. Do not deploy to production without a new user request.

Agents interrupted at the pause: `design_foundation` (Blazor inspector, structural SCSS and related tests) and `fuel_timeline_finish` (JS inspector, marker rendering and tests). `design_pages` completed browser geometry assertions and was awaiting a fresh artifact.
