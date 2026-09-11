# Map visibility and saved-route fuel release — September 9, 2026

## Scope

This release combines the saved-road v21 fuel changes documented in
[the fuel follow-up](fuel-saved-route-2026-09-09.md) with the larger circular stop
markers and stronger road strokes documented in
[the map follow-up](map-circles-visibility-2026-09-09.md).
Hybrid imagery resumes at zoom 15, after camera idle and without redundant map-type
updates. Equivalent route detail levels still reuse geometry.

`Next loads` starts off and now hides future recommended fuel visits as well as
future load routes. Each visit is filtered by its dispatch before grouping a
physical station; a current and a future visit at the same station stay independent.
Legacy visits need a matching current before-stop ID. Unknown ownership is hidden
only while future loads are off. Toggling preserves server amounts, numbering,
prices and the full assigned-load planning horizon, without recalculation. Displayed
progress is retained so a passed visit cannot reappear after a toggle.

## Verification

- Full local release gate: 1,164 Server, 485 Client C#, and 239 Node tests passed,
  including architecture checks (1,888 total; no skipped automated tests).
- Strict solution build passed with zero warnings and errors.
- A fresh published artifact verified 231 static assets, their compressed variants
  and six generated JavaScript dependency graphs.
- Twelve public GET checks across `amftms.web.app` and `tms.amfcarrier.com`
  matched the exact local artifact SHA-256 for index, Fleet Map fallback, CSS,
  fleetMap.js, gpuScene.js and Client WASM. Both liveness endpoints returned 200.
- The production page reloaded and rendered the new circles and routes; future
  routes toggle on/off. This does not establish the economics of an individual
  fuel recommendation.
- The 40 offline visual scenarios in the map follow-up preceded the final
  zoom/visibility changes. The optional full UI gate and authenticated lifecycle
  soak were not rerun for this release. No isolated PostgreSQL fixture or production
  performance benchmark was run.
- Local Client was rebuilt coherently and restarted at port 5067; map HTTP 200.

## Deployment

Firebase Hosting deployment completed from the exact verified directory:
`artifacts/release.PERXZB/publish/wwwroot`.

API Cloud Build: `98846d13-b448-43eb-8e23-f79aa85ac42c`.
Cloud Build completed all gate phases and deployed API revision
`amftms-api-00088-vhz` with 100% traffic. Exact image digest:
`sha256:f1ee71e8b6aeefc4119433a74b087f54d2280ed99d527054546112b5546b909f`.
Previous revision: `amftms-api-00087-fg7`.
Cloud Run reported Ready/ConfigurationsReady/RoutesReady; both public domains and
the direct service URL returned `200 Healthy` after rollout. A bounded initial
error-level log query for this revision returned no entries; this is not a soak test.
No manual fuel
recalculation, profile change or database repair was performed as a deployment step.
