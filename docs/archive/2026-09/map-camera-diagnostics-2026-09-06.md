# Camera rendering comparison

Inspected the user's open local AMFTMS and Samsara maps in Brave using the public Maps API via DevTools. No Samsara settings or application data were changed.

- Local map before: `getRenderingType() === 'VECTOR'`, `isFractionalZoomEnabled === true`.
- Samsara active map: `getRenderingType() === 'RASTER'`, `isFractionalZoomEnabled === false`.
- Local map after: `RASTER`, fractional zoom disabled, confirmed after reload.

A temporary AdvancedMarkerElement and our actual truck overlay were placed at the same coordinates with matching anchors. During a 180px programmatic pan, their DOM bounding rectangles were compared for 1.2 seconds using requestAnimationFrame. Both temporary markers were removed afterwards.

| Mode | Sampled frames | Maximum marker separation |
| --- | ---: | ---: |
| Vector | 73 | 0.2132 px |
| Raster | 73 | 0.1911 px |

Raster's largest sampled frame interval was 17.6ms. These are one-run diagnostics with DevTools open, not a general FPS benchmark. Marker-to-marker separation does not measure separation from the vector basemap, nor prove perceptual improvement in all drag/zoom gestures.

The local change only selects Raster and integer zoom. It does not replace truck animation, increase polling, or alter route/ETA computation. The visible tradeoff is stepped zoom levels instead of fractional zoom. Live user confirmation of camera feel remains necessary; no server deployment was performed.
