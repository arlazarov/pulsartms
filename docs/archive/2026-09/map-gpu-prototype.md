# GPU map (default)

Open `/fleet/map`. GPU rendering is now the default. The renderer query switch and
synthetic TEST vehicle were removed; old query URLs no longer enable another mode.
Source lives in `Client/Map` and builds into the local GPU bundle.

## Implementation

- Google vector map and one foreground deck.gl GoogleMapsOverlay (`interleaved: false`).
- Stations: ScatterplotLayer, radius 7 CSS pixels, no zoom-dependent radius.
- Trucks: IconLayer and TextLayer. The existing playback calculations feed these layers.
- Route geometry: PathLayer through an adapter retaining existing progress and route calculations.
- Static station data retain their array identity during truck animation to avoid re-uploading station attributes.
- The fleet map always injects GPU factories. Lower-level legacy factory defaults remain for tests and are not another user-selectable map.
- The bundle is local and loaded when opening the map. Build with `npm run map:build` (also a Client build target).
- Drawing order in the foreground canvas: stations, route geometry, stops, trucks/names, distance labels. HTML details stay above the canvas.
- Details are a fixed HTML card; no camera synchronization, clipping mask or animation loop.
- Unchanged truck metadata/visibility/selection skip scheduling; unchanged route layers retain their instances during truck-only updates.

## Maintenance boundaries

- `Client/Map/gpuScene.js`: vendor imports and public factory; `scene.js`: scheduling, layer composition and map adapters.
- `layerCache.js`: bounded, single-entry caches per layer group. `stopData.js`: immutable display snapshots after marker-content mutations.
- Stations, stop circles/numbers, distances and trucks invalidate independently. Truck motion does not read stop DOM or rebuild static layer instances. Distance updates leave stop geometry and truck layers intact.
- No scene-level zoom listener: GoogleMapsOverlay owns camera synchronization. Retina rendering stays enabled; all stations remain visible when the filter is enabled.
- Stop numbers are not separately pickable; their underlying circle handles selection.
- `detailsCard.js` owns fixed HTML details and cleanup. Its SCSS lives in `_fleet-map-details-card.scss`; station-specific content stays in `_fleet-station-popup.scss`. Generated bundles/CSS are build outputs, not editing targets.
- `gpuScene.test.js` checks 120 motion updates with unchanged static-layer identities, independent distance/selection updates, stop picking and disposal. These are regression checks, not GPU timing measurements.

## Historical prototype verification and limits

Tested in the Codex in-app browser on 2026-09-06:

- Confirmed actual Google `VECTOR` rendering, not a raster fallback.
- Real station set, truck 11006 and its route render together.
- Zoom in/out, keyboard pan, station popup and synthetic TEST movement exercised; no console errors observed.
- Point radius stays visually constant across the sampled zoom levels.
- Existing 31 JS tests pass and Client builds without warnings/errors.

Continuous trackpad behavior and frame timing in Brave still need validation. No FPS or latency claims are made.
The current map no longer uses the experimental layout described by those historical checks.
Truck labels intentionally allow overlap. Recommended station markers still use native markers.
GPU/CPU timing and large-fleet performance remain unmeasured; do not infer FPS improvements from build or unit tests.

The initial deck.gl 9.4.0 trial did not display layers in the shared context in this environment.
9.1.14 is pinned for this experiment and rendered successfully.

## References

- https://deck.gl/docs/developer-guide/base-maps/using-with-google-maps
- https://deck.gl/docs/api-reference/google-maps/google-maps-overlay
- https://deck.gl/docs/api-reference/layers/scatterplot-layer
- https://developers.google.com/maps/documentation/javascript/webgl/webgl-overlay-view
