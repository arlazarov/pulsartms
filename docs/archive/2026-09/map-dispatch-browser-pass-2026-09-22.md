# Map and Dispatch browser pass, September 22 2026

A bounded pass over the map and Dispatch screens as a user works them:
opening each page, switching trucks and stops, returning to a page, and
opening details. The intent was to measure that work in a browser and fix
only reproducible problems.

What the pass actually found is that the browser layer could not be
measured, because the probes that measure it had stopped running. Nine of
the ten offline probes run here failed, most of them before reaching a
single assertion. Two are repaired and pass; the rest are recorded with the
change that broke each one.

## Environment

- Repository at `2f1af38`, no application code changed by this pass.
- Staged Client: `artifacts/managed/release-Cu0ql1/publish/wwwroot`,
  published 2026-09-22 15:01, after the last Client commit `5b7d21c`.
- Probes run from `Client` with `UI_TEST_BROWSER_CHANNEL` unset, so
  installed Chrome.
- Node 23.11.0, Playwright 1.63.

## What could not be measured, and why

No live request timings were taken. A live pass needs a signed-in session
against a running API, and none was available to this session:

- The Chrome extension was not connected, so the owner's existing session
  could not be driven.
- The local API must not be pointed at the working `neondb` database from
  this session, and starting it with the stored secrets runs background
  workers that write business data.
- The isolated `pulsr_development` database has no user accounts, so the
  application cannot be signed into against it.
- Signing in to production would mean entering a password, which this
  session does not do.

So there are no cold/warm server latencies here. The per-action request
counts that would answer the duplicate-request, race and idle-polling
questions live in `hoursForecastSmoke` - it counts board, planning, summary
and total API reads, asserts that reselecting the same truck rereads
nothing, and asserts that a disposed map stops polling across sixteen
navigation cycles. Those assertions still do not run: see below. This pass
therefore reports no request counts and no timings of its own.

## Before: the offline browser suite was red

Each probe was run against the staged publish. What is named below is where
the run stopped, not the only problem in that probe.

- `uiSmoke` (`npm run test:ui`): the Dispatch table row has no
  `data-stop-id` for the next unfinished pickup. `c78d5e5` (09-21) moved
  the completion verdict to the server; the browser fixtures still state
  only `pickedUpAt`/`departedAt`.
- `mapToolbarSmoke`: the layer chips were expected to start with `IFTA`,
  and `/api/settings/planning` was unmocked. `29598ac` (09-19) took IFTA
  and the date off the map and had it read the price basis instead.
- `hoursForecastSmoke`: the map never started, so every case timed out on
  its marker. `fa9ad7b` (09-20) made `cameraViewport` TypeScript while the
  stub still inlined its source into a served JavaScript module.
- `mapMarkersSmoke`: one truck instead of four. The fleet is drawn in two
  icon layers, the selected truck and the rest; the fixture read only the
  first.
- `stopDetailsSmoke`: the current route popup never appeared.
  `/api/settings/planning` and the truck weather call are not in its
  fixture table.
- `nativeInspectorSmoke`, `stationPopupSmoke`, `stopCardsSmoke`: the tank
  percentages read empty. `4951d0f` (09-20) renamed
  `fleet-fuel-visit__percent` to `__level` in the station card.
- `fuelEditorSmoke`: unmocked `/api/settings/planning`, then the truck
  card's disclosure control, which the probe asserts is absent.
- `mapStartupSmoke`: passed.

`mapLifecycle` is not in this table: it drives a running local application
and asks for a sign-in, so it was not run. Twelve further offline probes
exist - appearance, brand, dispatch creation and workspace, map remount,
fuel marker visibility, page transition, route editor, station quote sizing,
truck playback, mobile truck scrolling and truck readings - and were not run
in this pass, so their state is unknown.

Only `uiSmoke` is wired into a gate, through `PULSARTMS_RELEASE_UI=1` on
`verify-release.sh`. The others are run by hand, which is why they drifted
without anyone seeing it. The practical effect is that the map and Dispatch
work of the last three days was not browser-verified at all.

## After

- `uiSmoke`: 12 cases, no failures, no browser errors, no unexpected
  requests. Run twice.
- `mapToolbarSmoke`: 16 cases, no failures. Run twice.
- `hoursForecastSmoke`: four causes fixed. It now boots the map and reaches
  the selected-truck layout stage instead of timing out at startup, and
  still fails there; see below.
- `mapMarkersSmoke`, `stopDetailsSmoke`, `nativeInspectorSmoke`,
  `stationPopupSmoke`, `stopCardsSmoke`, `fuelEditorSmoke`: each moved past
  its first cause and each is still red at a later assertion.

### What the repaired probes check again

`uiSmoke` boots the staged Blazor application for Dispatch, Users, Settings,
Fleet Map and Add User at 1440/390/2344px, both themes, 100%/200% text. Its
Dispatch checks include the repeated-visit fixture, the Cards/Table/Papers
views, completed views not starting live planning, and the stop workspace.
`mapToolbarSmoke` checks the map toolbar at four widths, both themes and two
text sizes, including preference restore across a reload and that toolbar
interaction never replaces or moves the map element.

### What is still red in `hoursForecastSmoke`

The remaining failure is not a fixture gap. The selected-truck card opens
collapsed at every width. Compiling `pages/fleet-map/stage` and
`pages/fleet-map/inspector` gives the collapse rule twice: once at the top
level, outside any query, and once inside
`@container map-truck-card (width < 40rem)`.

```
444: .fleet-map-inspector[data-inspector-mode=truck].is-mobile-collapsed
       .fleet-map-info-content { display: none }
737: @container map-truck-card (width < 40rem) {
748:   ...is-mobile-collapsed .fleet-map-info-content { display: none }
```

The unscoped copy in `inspector/_hours-line.scss` makes the scoped one in
`inspector/_narrow.scss` redundant, and it also gives the chevron
`display: inline-flex` at every width. Measured in the probe at 2344px,
selecting a truck leaves `#fleet-map-telemetry-details` and
`#fleet-map-route-details` not visible: the readings, GPS line and route
facts are behind the chevron on a desktop card.

Three other things say the opposite. `Client/tests/browser/README.md`: "The
default desktop compact view keeps speed, fuel and engine readings aligned
and visible" and "desktop keeps all content visible". `hoursForecastSmoke`
and `fuelEditorSmoke` both assert that "the always-visible truck card has
no Details or Hide control". And `_narrow.scss` scopes the collapse to a
narrow card, which only makes sense if a wide one does not collapse.

Against that stands `df98fd7`, which deliberately made the closed card
carry the hours and the arrival, so a closed desktop card may be intended.
Which behaviour is wanted is the owner's call, so this pass changed neither
the stylesheet nor the guide's design claims, and left the probe asserting
the documented behaviour. It fails until that is answered.

The duplication predates the September 20 stylesheet split: the pre-split
`_compact-inspector.scss` already carried the rules unscoped at lines 154
and 163 and scoped again at 487. The split moved them, it did not cause
this.

## Shared map and Dispatch reads

Read from the source, not measured in a browser, and recorded here only so
the next pass does not repeat it. None of it is offered as a UX check.

- `PlanningDisplayCache.Clear()` is called from `AuthService` alone, on
  sign-in and sign-out. A truck change goes through `Invalidate(truckId,
  dispatchId)` or `StoreRecalculated`, both of which touch only entries
  matching that truck, dispatch, execution leg and assignment revision. No
  path drops the whole cache for one truck.
- `FleetMap.SelectRouteAsync` takes a selection version, cancels the
  in-flight preview and route requests, and re-checks that version after
  every await, so a late answer for the previous truck cannot land.
- The map polls `/api/fleet/locations` and the selected truck's planning
  every 10s while the page is visible, and upcoming loads every 30s, with
  the known plan id and version sent so unchanged geometry is omitted.
- Dispatch polls board telemetry every 10s and reloads the board itself
  every 60s, both suppressed while the search debounce is pending.

## Commands

```
cd Client
MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot node tests/browser/uiSmoke.mjs
MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot node tests/browser/mapToolbarSmoke.mjs
HOURS_TEST_FLEET_ONLY=1 MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot \
  node tests/browser/hoursForecastSmoke.mjs
npm test
npm run format:check
```

`bash test.sh map` passed: 286 Client.Tests, 220 Server.Tests, 64 Node
tests. No shipped code changed, so it is hygiene rather than coverage of
this change.

`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` was then run on the
committed tree and passed: 1054 Client.Tests, 3219 Server.Tests, 625 Node
tests, the strict Release builds, 297 published assets and 11 entry-point
dependency graphs, and `test:ui` with its twelve cases. That is the first
time the gate's browser step has been reached. Its authenticated browser
check is separate and was not run: it needs `PULSARTMS_RELEASE_BROWSER=1`
and a local API and client origin. Passing the gate does not authorize a
release, and none was made.

## Limits

Fixture replies are instant, so nothing here measures server or provider
cost. Passing probes do not establish production layout, live authentication
or real data. Seven probes remain red, and their later assertions have
still not run; twelve more were not run at all.
