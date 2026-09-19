# Client JavaScript architecture

## Boundaries

- Blazor owns API requests and page state.
- `fleetMap.js` coordinates independently owned layers and guards asynchronous updates.
- `trucks/` owns telemetry buffering, playback and follow-camera behavior.
- `stations/` owns price presentation, selection and recommendation rendering.
- Route geometry calculations are pure modules; the route layer owns drawing and timing.
  `geometry/routeGeometry.js` builds joined paths, provider-mile progress and stop
  anchors. `routes/routeStops.js` owns current-stop markers and details, and updates
  names, appointments and tracking without rebuilding unchanged road geometry.
  `routes/nextLoadDisplay.js` prepares individual upcoming stop numbers and mileage;
  `nextLoads.js` retains visibility, selection, render objects and disposal.
  Future stops use click selection only. Selecting a stop pins that load's roads,
  pickup/delivery markers and every stop label; coincident visits retain separate
  numbered circles, each selecting its own stop identity. Blank-map and truck clicks, hiding Next Loads or
  removing the selected load clear the inspection. Shared labels and geometry
  refreshes retain a still-present selected load.
- `Client/Scripts/fleetMap/rendering` owns the GPU scene. Only `gpuScene.js` imports vendor libraries.
  All fuel station points render above route lines and their outlines, including
  highlighted future routes. Recommended stations retain priority over ordinary
  stations; pickup/delivery badges and trucks remain above fuel points. Layer
  ordering does not change price colors, selection callbacks or route geometry.
- `rendering/sceneMetrics.js` owns map dimensions in CSS pixels and shared label
  rasterization settings. Device density is handled by the overlay, not by
  multiplying label sizes. Each fixed-size font is rasterized at its displayed
  physical size, retaining small-glyph hinting without SDF softening. Bilinear
  edges and nearest mip-level selection avoid additional level blending.
  A density change invalidates the scene's single-entry typography cache;
  ordinary resizing, camera motion and telemetry reuse the font settings.
  Stop label cards read the existing body typography, semantic color theme
  roles and spacing/radius tokens through a scoped style probe. Their regular
  text is left-aligned inside a card centered above the marker. Width is measured
  only when label data or theme/font inputs change. Theme observers and ETA expiry
  timers are released with the map; truck and fuel typography remain independent.
  Pickup/delivery headings and borders use their corresponding theme roles.
  ETA uses success/danger only with a confirmed server appointment and lateness
  verdict; missing verdicts and retained forecasts during route updates use the
  neutral ETA accent. Text rows carry explicit tones, never colors parsed from
  their contents. Each visibility group has one opaque background layer and one
  colored text layer; unchanged text and tones retain their cached render data.
- DOM cards and native camera dialogs retain separate lifecycles.

## Consolidation

Segment projection and route-position matching are shared, independently tested
calculations. Distance labels use one rounding and conversion function. Blazor
callback error handling has one implementation. Removed unused RGB interpolation
and obsolete route-marker arguments. Heading-aware snapping intentionally retains
its own acceptance rules rather than reusing clamped nearest-point matching.

## Lifecycle

Layers release their own listeners, frames and render objects. Fleet disposal
releases layers before finalizing the GPU scene. Station render batches use version
checks. Route and options continuations recheck versions after awaiting work.
Truck, route, station, next-load and details-card APIs reject updates after disposal.
The Google loader is application-scoped and bounded to three attempts; it is not a
map-owned polling loop.

## Source ownership and build output

All maintained browser JavaScript lives in `Client/Scripts`:

- `fleetMap/fleetMap.js`: the Blazor entry point and lifecycle composition.
- `fleetMap/provider/`: Google loading and retained map ownership.
- `fleetMap/rendering/`: GPU scene, layers, appearance and GPU picking.
- `fleetMap/geometry/`: pure coordinate and route calculations.
- `fleetMap/routes/`: current and upcoming route presentation.
- `fleetMap/trucks/` and `fleetMap/stations/`: feature-owned state and behavior.
- `fleetMap/ui/`: shared inspector ownership, camera insets and distance labels.
- `fleetMap/lifecycle/`: cooperative browser scheduling.
- `dispatch/`: Dispatch browser interop.
- `shared/`: reusable popup scroll locking, native dialog interop and atomic
  authentication-session storage.

`build/javascript.mjs` bundles these entry points with ESM splitting. GPU code
remains dynamically imported; shared chunks are generated, not hand-maintained.
`wwwroot/js/generated/` contains build output only. Local builds retain previous
content-hashed chunks because open tabs may still import them. Blazor imports the
generated entry URLs. `npm run js:build` is the canonical command; `map:build` is
retained as a compatibility alias. Release publishing uses a new output directory
and verifies the published asset hashes and current entry-point dependency graphs.
Every generated module imported by Razor or Client C# must be registered as an
entry point; the architecture check compares those imports with the build graph.
The shared load dialog reuses native-dialog interop and the existing reference-counted
popup scroll lock. It restores focus and scroll on close without owning load data
or fetching a second copy of the selected load.

Tests mirror their subject: `tests/fleetMap`, `tests/styles`, `tests/architecture`.
Authentication storage checks live in `tests/identity`. The small `authStorage.js`
module owns the origin-wide Web Lock around session read/migration and
compare-and-set writes; it does not send HTTP or manage application authentication
state. Missing Web Locks fail closed, so authentication requires a supported browser
and secure origin. Blazor keeps refresh and account/lifetime guards.
The unused history renderer and prefix helper were removed together with their
isolated tests after confirming they had no runtime callers. The details-card
factory is imported directly; there is no rename-only `infoPopup` module.

Failed initialization releases resources already created, using the same reverse
cleanup order as normal disposal.

`mapHost.js` retains one provider map and detached container per browser document.
Mounts are exclusive; release is idempotent. Reopening resets mutable map options,
not constructor-only map ID or rendering type. Feature layers, callbacks and GPU
scenes remain mount-scoped and are disposed before detaching the provider host.
Each owner removes its own listeners; broad provider listener deletion is avoided.
The host fills the map viewport independently of content height.

`fleetMap/contracts.d.ts` documents the route interop payloads. `setRouteBytes`
returns whether it applied the update. A `geometryOmitted: true` payload must match
the retained plan ID, version and truck ID; otherwise JavaScript returns false
without mutation and Blazor resends full geometry. Accepted metadata replaces
nullable fields while preserving only route and reference-route geometry. Clearing
selection releases that retained plan. Late/disposed updates return false.
`Client/jsconfig.json` checks the pure geometry, payload and next-load preparation
modules and their imports. This is targeted JavaScript checking, not full Client
JavaScript type coverage.

Stop markers receive stable scene IDs. Each opaque 34px circle has a separate
centered number layer; its shape does not depend on the digit count. Current stops
remain above future stops unless a future stop is highlighted. Coincident circles
use a stable two-column screen-space layout, with a centered final odd circle and
small dots at their unchanged geographic anchors. Highlighting reorders existing
marker layers without changing their data. Number/job changes update only that
marker and any coincident circles whose spacing changes. Removed markers
are evicted from the scene cache, and camera/truck motion reuses the unchanged group.

Truck markers use a green heading arrow at speeds of at least 1 mph. Stationary
trucks use green circles while idling and gray circles when off or unknown.
These three shapes are cached; unit labels remain upright and independent.
Truck playback keeps a 90-second telemetry buffer for minute publication and
client polling jitter, and advances at normal time without catch-up acceleration.
Hidden tabs stop frames. Visibility restoration, a frame suspension over one
second or history pruning rebases playback before painting; missing journeys
must not become correction animations. Follow retains its captured screen anchor.
When GPS runs out, hold the last measured point rather than invent movement.
Fuel circles retain their existing price colors and every original selectable point.
All fills are 16px across; the popup retains the complete price quote, while map
labels show only fuel order or active editing. Camera changes do not rebuild the
station layers. After a direct-hit miss, a bounded GPU query retains the prior
20px mouse target and the larger touch tolerance without visible extra circles.

Server ETA metadata reaches current and future stop labels separately from route
geometry. Exact dispatch/stop IDs and plan/truck revision guards prevent cross-load
reuse, and a local expiry timer removes ETA text at its server validity deadline
without requesting providers. Labels place company/stop identity before ETA,
per-leg Empty or Leg distance, then cumulative Total distance; both distance units
remain on their respective rows. An unavailable estimate does not invent a time.

Route fitting checks the truck layer's current Follow state at the point of camera
mutation. A delayed planning response may update geometry without overriding an
active Follow camera; no duplicate Follow state is kept in the route layer.

Clicking a truck group frames only that group's positions inside the unobscured
map viewport, capped at zoom 18 for coincident trucks. It preserves the selected
truck and route, releases Follow and cancels a pending initial fleet fit. Route
geometry and other trucks never enter the group camera bounds.

`ui/cameraViewport.js` caches the map and top-info/fuel-editor rectangles, choosing
the largest unobscured region for intentional camera focus and route-fit padding.
Resize observers include the persistent info wrapper while hidden; a direct-child
observer tracks inserted or removed cards without watching provider DOM churn.
Follow frames use cached geometry only. Overlay appearance, disclosure and data
loading refresh cached insets without moving the current camera, including an
untouched first truck focus. Only an actual map width/height change notifies the
active focus to reframe. Later explicit focus and route-fit actions use the updated
insets; manual gestures and route fits release temporary truck focus.
All observers and the window resize listener are disconnected with the map session.

`ui/dockedDetails.js` targets one persistent, explicitly JavaScript-owned native
host inside the Blazor inspector. Current-stop and fuel layers reuse their existing
HTML renderers through injected popup factories; they do not create separate lower
cards. Only an explicit selection activates a native owner. Polling may update that
owner's content, but inactive show/hide calls cannot replace another selection.
Truck, future-stop, native-card and close transitions publish monotonic revisions
with the tracked truck identity. Blazor rejects stale revisions or another truck's
callback. Closing inspection clears only detail selection, not the current route.
Explicit Back, Close and native Escape return focus to the existing map only when
focus belongs to the departing inspector. They prevent page scrolling; marker
selection, passive owner cleanup and polling do not move keyboard focus.

## Verification scope

Node tests cover geometry, playback, follow, rendering caches, recommendations,
late updates and disposal. Client builds use warnings as errors.
Regressions also cover same-version stop metadata, omitted-geometry identity
rejection, pending-stop numbering and per-stop layer/data identity. These checks
measure object and data reuse, not browser GPU time or production frame rate.
Earlier authenticated browser runs covered map startup, truck selection, Follow,
deselection and leaving/reopening the page; their historical results are recorded
in `Client/tests/browser/README.md`. They are not acceptance results for subsequent
changes. Repeat relevant browser checks against the release artifact. This document
covers the JavaScript boundary only, not server correctness or deployment approval.
