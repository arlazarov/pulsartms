# Fleet Map remount camera synchronization

Local implementation only; no deployment or database changes.

## Finding and scope

The reported Dispatch-to-map screenshot shows distant truck markers compressed
into one area. The retained provider map and newly created GPU scene have
independent lifetimes. The installed GoogleMapsOverlay 9.1.14 initializes Deck
with a default viewport until a native provider frame supplies its camera.
If device initialization follows the last stationary-map frame, layers can paint
with that default projection until the user pans or zooms.

An offline browser fixture with the actual installed adapter reproduces this
startup ordering. Holding the provider frame leaves Deck at zoom 1, projecting
the two distant synthetic trucks less than 100 pixels apart. Releasing the
frame applies the retained camera and separates them by more than 600 pixels.
The live incident was not reproduced: the app browser control could not start
its runtime. The screenshot's specific cause therefore remains an inference.

## Change

The scene filters layers until its GPU device has loaded and the provider has
delivered a camera frame. A transient empty native overlay requests one redraw
and detaches after that frame or scene disposal. Late callbacks cannot restore
disposed layers. Device replacement repeats the same handshake.

This uses the public WebGLOverlayView redraw/lifecycle API; see the
[Google reference][webgl]. Production does not inspect adapter private fields,
move the camera, refit the route, poll, allocate another canvas or request data.
The fixture alone reads Deck's viewport for assertions. Truck coordinates,
clustering, route geometry, selection and business calculations are unchanged.

[webgl]: https://developers.google.com/maps/documentation/javascript/reference/webgl

## Verification

- `bash test.sh map`: 253 Client and 125 server C# checks; 331 map JavaScript
  checks and 47 JavaScript architecture checks passed.
- `npm run js:check` and `npm run js:build`: passed.
- Strict Client build: passed, zero warnings and errors.
- `mapRemountSmoke.mjs`: eight mount cycles across DPR 1 and 2. Pending layers
  remain hidden; the ready projection matches the retained camera. Each active
  mount has one canvas and overlay, with zero camera writes. Disposal leaves
  zero canvases, overlays and listeners; the provider map is created once.
- `mapMarkersSmoke.mjs`: both GPU rendering/interaction cases passed.
- `mapStartupSmoke.mjs`: desktop and mobile camera lifecycle cases passed.
- Remount screenshots were inspected; no live provider/API calls were made by
  these deterministic browser fixtures.

Disposable evidence is under the managed runs `browser-map-markers-dS02sR`,
`browser-map-markers-o6Ebko` and `browser-map-startup-w07eoG`.
This is scoped map verification, not a full-suite or production-performance
pass.
Live Google Maps, authenticated navigation and PostgreSQL checks were not run.
