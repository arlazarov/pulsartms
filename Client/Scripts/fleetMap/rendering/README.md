# Fleet rendering boundary

`gpuScene.js` is the only vendor entry point. esbuild produces
`wwwroot/js/generated/fleetMap/rendering/gpuScene.js` and shared chunks; never edit generated files.

- `scene.js`: scene state, scheduling, selection and lifecycle; renderer ports.
- `sceneLayers.js`: deck.gl layer definitions and per-scene layer caches, with
  stable per-stop circle/number pairs. Reordering a highlighted stop preserves
  layer/data identity; changing a number or job updates only that pair.
- `stopData.js`: immutable snapshots of explicit stop data, retaining unchanged
  rows by scene ID without DOM observation.
- `truckAppearance.js`: three cached 28px states: moving green heading arrow,
  stationary green idle circle, and gray off/unknown circle. Speed determines
  movement; truck number typography is independent.
- Station fills use uniform 16px price-colored circles. Prices remain in the
  station popup; map text is limited to fuel-order and active-edit badges.
  Camera changes do not scan stations or invalidate their cached layers.
- `layerCache.js`: single-entry identity cache.
- `stationTouch.js`: nearest-station picking after a direct-hit miss, preserving
  the prior 20px mouse target and larger touch tolerance without visible halos.

Browser orchestration lives in `Scripts/fleetMap`, with telemetry/playback in
`trucks` and prices/recommendations in `stations`. These receive renderer ports
from the scene rather than selecting a fallback renderer. Blazor owns page state
and API requests; JavaScript owns map interaction and animation.

Shared planar segment projection lives in `fleetMap/geometry/segmentProjection.js`.
`geometry/routeGeometry.js` prepares paths, provider-mile progress and stop anchors.
`fleetMap/geometry/routePosition.js` owns bounded nearest-segment matching and progress
interpolation. The route layer owns scheduling, search-window selection and drawing;
the pure matcher has no map, clock, DOM or mutable state dependencies.
Detail simplification and live progress use it without allocating
temporary point objects in their inner loops. Callers retain coordinate scaling,
distance units and acceptance thresholds. Heading-aware truck snapping deliberately
keeps its unclamped projection: points beyond segment endpoints must be rejected.

Prefer sharing pure calculations and stable lifecycle contracts, not merging
controllers merely because they both use timers or event listeners. Native camera
dialogs, scroll-locking page popups and map details cards have different lifecycles.

The fleet facade owns layer disposal. Station rendering invalidates in-flight
batches on disposal, releases retained data and ignores subsequent calls; repeated
disposal is safe. Async facade operations recheck disposal after yielding before
touching another layer. Tests cover disposal during a station render batch.
Truck and next-load layers also reject updates after disposal. Route updates
recheck their version after awaiting recommendations before applying progress;
an older route must never advance a newer route's fuel recommendations.

Details reuse HTML in the shared top inspector, styled by the Fleet page's SCSS owner.
Recommendation rings and distance annotations, ordinary station points, trucks
and route stops all use the single GPU scene. Stop circle/number pairs remain
ordered together so overlapping markers stay opaque and readable.
Static DOM presentation belongs in SCSS; GPU appearance belongs in sceneLayers.

`routes/routeStops.js` updates current-stop metadata independently of geometry.
`routes/nextLoadDisplay.js` prepares upcoming stop grouping and mileage without
owning render objects. `routes/routePayload.js` enforces the Blazor contract for
retained geometry; its identity check runs before any scene mutation.

Run `npm test`, `npm run js:check`, `npm run js:build`, and `npm run styles:build` after changes.
Tests inject explicit ports; no legacy renderer is shipped for test compatibility.
