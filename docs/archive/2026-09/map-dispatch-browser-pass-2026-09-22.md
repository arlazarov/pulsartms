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

## Second pass: the card's contract, and the probe's real assertions

The owner settled the open question: the selected-truck card is meant to be
closed at every width, showing the top line and the miles to the next stop,
with a disclosure for the rest. That is the contract this pass then held the
probe and the guide to.

### What changed

`hoursForecastSmoke` now opens the card the way a dispatcher does. A helper
clicks the chevron and checks it opened; the cold-selection check first
asserts the closed card still carries the load, the miles left and the four
clocks, and only then opens it to look at the readings, the location and the
route placeholders. Every new selection closes the card again - including
reselecting the same truck - so the probe reopens it where it reads the
lower section, and asserts that closing behaviour rather than working
around it.

`measureTruckControls` used to assert the card had no disclosure at all. It
now asserts the opposite contract and more of it: exactly one chevron and no
Details/Hide wording, the lower section hidden while closed, the driver and
trailer still readable in the closed card, the chevron's own bounds
unchanged between the two states, and every lower group visible once open.

Nine further expectations were moved to where the September 19 redraw put
the thing they describe: the load and its order and the miles left now read
in the head, so their busy state, their label/value pair and their geometry
are read there; the vehicle line closes the card instead of opening it, so
the gap is measured route-to-vehicle; the delivery window belongs to the
visit, not the timing column; the ETA reads in the head; the distance column
says `Run`, not `Remaining`; Close now stands beside Back in a stop
inspector; and a phone card stacks its groups instead of placing them
side by side. `Client/tests/browser/README.md` was corrected to the same
contract.

Where a part of the card no longer exists, its check now fails by name
instead of throwing on a null and taking the rest of the run with it.

### What the probe now establishes

One case at 1440px completes, including the sixteen-cycle lifecycle loop.
Nothing in the request, race, polling, pending/success/failure, ETA or fuel
families fails; `unexpectedRequests` is empty and there are no browser
errors. Specifically, these ran and passed:

- Opening Dispatch reads one summary batch and no per-truck planning.
- Reselecting the same truck rereads neither planning nor weather.
- A held preview, a held load-details read and a held planning response are
  released in that order; the card keeps the previous values through each
  pending stage.
- A held response that crosses the fixture's `ValidUntil` does not change
  the displayed ETA text, times, status or colours - two quiet samples on
  Dispatch and five on the map, three retained cards each.
- A pending Dispatch response is polled, and polling does not erase or
  replace the retained board stop times.
- With the map disposed, thirty seconds of fast-forwarded time adds no
  planning read - asserted on each of sixteen cycles.

### Sixteen cycles of Dispatch to map and back

Timings are the staged Client's own cost against instant fixture replies on
the host clock, which `page.clock` does not move. They are not server or
provider time.

| | cold (cycle 0) | median of 15 repeats |
| --- | --- | --- |
| open Dispatch | 69 ms | 61 ms |
| open Fleet Map | 98 ms | 64 ms |
| select the truck | 54 ms | 44 ms |

Retention across the sixteen cycles, after a forced collection each time:
JS heap 5.8 MB to 6.4 MB, DOM nodes 1210 to 1211 (one sample at 1342),
listeners 36 throughout, documents 4 throughout. API reads grow by exactly
14 per cycle, from 92 to 310 - constant per round trip, not accumulating.

### What still fails, and why

Every remaining failure is layout or type, and each traces to a change the
owner made on September 19-20.

- **The head grows when the ETA lands, 8 selectors x 3 stages.** Measured:
  `.fleet-map-inspector__arrival` is 19.8px high with the placeholder and
  36.4px with the forecast, so the header goes 78.8 to 83.2 and every row
  below it moves down 4.4px. The placeholder in `FleetMap.razor` exists to
  reserve that space and reserves one row where the loaded forecast takes
  two. This one looks like a real defect rather than a stale expectation,
  but the fix changes the card's resting height, so it is left for the
  owner.
- **Type scale, 10 messages x 2 phases.** The readings are 14px where the
  fixture expects 16px, and several selectors it names (`__load-reference`,
  `__metric > strong`, `__total > strong`) no longer exist. The card head
  was rebuilt to the drawing in `3d0d50d`; the new numbers are a design
  decision and were not invented here.
- **Card layout, about ten messages.** Dials (the clocks are text now),
  the duty line (the map asks `DriverHours` for clocks without it), column
  dividers, and the two-column expectations all describe the previous card.
- **A phone card scrolls sideways.** At 390px the card's scroll width
  exceeds its client width by 73px, and the widest thing past the content
  edge is `.fleet-map-mobile-summary__distance` at 32px - the "Left 120 mi
  · 193 km" group in the head. `_hours-line.scss` keeps that group on one
  line on purpose, so that it never breaks between a number and its unit;
  on a phone showing both units there is not room for it. The assertion
  now names the offender, and it stops the two 390px cases.

### `fuelEditorSmoke`

The same contract applies to the truck card the fuel editor opens beside,
so its `assertTruckInformation` was turned round too: one disclosure,
opened by the same click, and then the drawn order of the lower section -
the route, the actions under the detail they act on, and the vehicle line
last. It moves two failures further and then stops on its selected-truck
HOS block, which measures four dials. The map asks `DriverHours` for text
rather than dials, so that block describes the previous card and needs the
same kind of rewrite; it was not attempted here.

### Checks

```
cd Client
HOURS_TEST_LIFECYCLE=1 MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot \
  node tests/browser/hoursForecastSmoke.mjs
HOURS_TEST_FLEET_ONLY=1 MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot \
  node tests/browser/hoursForecastSmoke.mjs
FUEL_EDITOR_CASE=1440-1000-light \
  MAP_TEST_ARTIFACT_DIR=<staged>/publish/wwwroot \
  node tests/browser/fuelEditorSmoke.mjs
```

### Blockers, exactly

- `hoursForecastSmoke` 390px cases: the phone card's 73px sideways scroll.
- `fuelEditorSmoke`: the selected-truck HOS block expects four dials.
- `mapMarkersSmoke`, `stopDetailsSmoke`, `nativeInspectorSmoke`,
  `stationPopupSmoke`, `stopCardsSmoke`: untouched in this pass beyond the
  first causes fixed earlier; each stops at its own later assertion.
- The head's 4.4px growth, the type scale and the card's column
  expectations need the owner's decision, not a guess.

## Third pass: the two defects fixed, and the probes brought up to the card

The owner approved fixing the two reproduced defects and finishing the
offline work. Both are fixed and measured; the probes that describe the
truck card were brought up to the card as it is drawn today.

### The head no longer steps down when the ETA lands

`.fleet-map-inspector__arrival` kept room for one line while the loaded
forecast takes two - the hour, and a word about the cycle under it - so
the header grew by 4.4px as the ETA arrived and every row below it moved.
The block now reserves both lines from the start, in the card's own type
tokens:

```scss
min-block-size: calc(1.4 * (#{ui.fs(body)} + #{ui.fs(small)}));
```

Measured before: header 78.8 -> 83.2, arrival 19.8 -> 36.4, and eight
elements moving at each of three loading stages. After: no element moves
at any stage, at any width. The two lines are 14px and 12px at a 1.4 line
height, which is exactly the 36.4px the loaded forecast measured.

### A phone card no longer scrolls sideways

At 390px with 200% text the card scrolled 73px. Three separate causes,
each fixed in the narrow container query the card already had:

- `--route-fact-label` was a fixed 7rem, which at 200% text is 224px of a
  312px card, so every value beside a label was squeezed. On a narrow card
  the label now takes `max-content` and the value keeps the rest.
- The miles-left group carried a fixed `route-progress` width, which is
  there to stop three groups abreast from resizing as numbers arrive.
  With one group to a row there is nothing to keep still, so it takes the
  row; and with the room to itself the phrase folds instead of ellipsising,
  so no reading is hidden. The gap before the second unit became breakable
  in `FleetMap.razor`; the gaps inside each reading stay non-breaking, so
  a number never leaves its unit.
- "Order 568349636" and the GPS address are single unbreakable phrases.
  The order's label may now step above its number, and the address folds.

A long street also widened the stop's column instead of ellipsising,
because a flex item does not shrink below its content unless told to:
`min-inline-size: 0` on the street and on the address block.

Measured at 390px/200% text: scroll overflow 73px -> nothing past the
card's edge and no horizontal scroll. The probe now asserts the symptom -
that the card cannot actually be scrolled sideways - and separately that
nothing reaches past its edge, and names the widest offender when it does.

### The probes now describe this card

Every expectation that still described the pre-September-19 card was
restated from the stylesheets and the razor comments that own it, not from
whatever the code happened to render:

- The clocks read as text in the head, so the vehicle line asserts it
  draws no clocks and no duty; the head asserts four text clocks, no
  dials, in one or two rows on a narrow card.
- A reading is a word and a value with no icon of its own; only the
  weather keeps an icon, because there the icon is the reading.
- The route reads first and the vehicle line closes the card; the actions
  sit under the detail they act on.
- One hairline divides the stop from the facts, drawn on the stop's inline
  end and turned underneath it when the card is too narrow for two columns.
- The stop keeps its address and its window; the facts beside it are the
  run's total, the cycle and the fuel on arrival.
- The type scale is read from the stylesheets: labels and echoes small,
  values body weight 600, only the unit number larger. The scale itself was
  not changed to suit a test.
- The workspace shows one pane at a time when it is too narrow for two, so
  a check that reads the itinerary presses the Stops tab first.

### What runs now

`hoursForecastSmoke` completes all twelve cases - 2344/1920/1440/1200/900/390
in both themes - where before this pass it reached none. The lifecycle run
completes its sixteen Dispatch-to-map cycles. No unexpected requests, no
browser errors. The probe gained a width and theme filter
(`HOURS_TEST_WIDTHS`, `HOURS_TEST_THEMES`) so one case can be re-run on its
own.

- `uiSmoke`: 12 cases, green, before and after.
- `mapToolbarSmoke`: 16 cases, green, before and after.
- `mapStartupSmoke`: green, before and after.
- `hoursForecastSmoke`: none of its twelve cases before; all twelve now.
- `fuelEditorSmoke`: stopped on the truck card's disclosure; now runs the
  card and its HOS probe and stops later.
- `nativeInspectorSmoke`: stopped on the tank percentages; now five of
  eight cases, stopping on a 390px/200% overflow.
- `stationPopupSmoke`: stopped on the purchase cost; now runs the planned
  card and stops on its gauges.
- `mapMarkersSmoke`: one truck instead of four; now runs trucks, stops and
  stations and stops on a cluster pixel check.
- `stopCardsSmoke`: stopped on the route colours; now runs further.
- `stopDetailsSmoke`: stopped on the current route popup; now stops on the
  load reference the map is sent.

### Measured again on the gate's own artifact

Sixteen Dispatch-to-map cycles, timed on the host clock against instant
fixture replies. These are the staged Client's own cost, not server time.

| | cold (cycle 0) | median of 15 repeats |
| --- | --- | --- |
| open Dispatch | 64 ms | 59 ms |
| open Fleet Map | 66 ms | 61 ms |
| select the truck | 38 ms | 45 ms |

Across the sixteen cycles, after a forced collection each time: JS heap
5.8 -> 6.4 MB, listeners 36 throughout, DOM nodes 1211 -> 1212. API reads
91 -> 313, about fifteen a cycle and not accumulating. The lifecycle case
itself finishes with no failures at all.

The Fleet Map's own cold open is now faster than the first pass measured
(66ms against 98ms), which is consistent with the head no longer being
laid out twice; three runs are not a claim of a speedup and none is made.

### Still failing, with what is known about each

Twelve soft failures remain in the twelve-case matrix - six distinct, each
in both themes - and no unexpected requests or browser errors.

- **390px, 200% text: the timing column overflows.** The inspector is
  312px wide and scrolls 326; `.fleet-map-route-info__timing` is 264 wide
  and scrolls 302. Letting the fact label shrink to `minmax(0, max-content)`
  cleared the rest of the card but not this column. Not fixed here.
- **390px: the outside temperature's position.** The check expects it
  after the third reading on the desktop row or in the mobile left column.
- **A Dispatch board row overflows** where the ETA carries a cycle status
  ("Cycle short", "Late by 1h 05m Cycle unknown").
- **Selecting a stop still moves an itinerary row** at one width, after
  the pane switch is accounted for.
- The four station and marker probes stop at their own later assertions,
  each on a part of the station card or the GPU layers that was redrawn:
  the planned card's gauges, a cluster's painted background, the docked
  inspector at 390px/200%, and the load reference the map is sent.

### Checks run on the committed tree

- `PULSARTMS_RELEASE_UI=1 bash verify-release.sh`: passed. 1054
  Client.Tests, 3219 Server.Tests, 625 Node tests, the strict Release
  builds, the published-artifact check, and `test:ui` with twelve cases and
  no failures.
- `bash test.sh map fleet dispatch routing`: passed - 937 Client.Tests,
  1926 Server.Tests, 12 + 64 Node tests.
- `hoursForecastSmoke` against the gate's own artifact: twelve cases, and
  the lifecycle case with no failures at all.
- `npm run format:check`, prettier on every changed file.
- Not run: the authenticated browser gate (`PULSARTMS_RELEASE_BROWSER=1`),
  which needs a running application and a signed-in session. No release
  was made and none is authorised.

Evidence is in the managed runs named above: `browser-ui-X6Aqus` for the
gate's UI step and the `browser-hours-forecast-*` runs for the matrix and
the lifecycle case, each with its report and screenshots.
