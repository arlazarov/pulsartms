# Truck panel probe migration

Scope: the ungated probes `hoursForecastSmoke.mjs`, `fuelEditorSmoke.mjs`
and the helpers `mobileTruckScrolling.mjs` and `truckReadingsLayout.mjs`
in `Client/tests/browser/`, after the old Fleet Map truck card was removed
(`a5f7b308`) in favour of the truck panel. Base `44da6099`
(`claude/futuristic-pulsar-ui-concept-80ccd4`). No production source was
changed. Continues [the September 26
migration](hours-forecast-probe-migration-2026-09-26.md).

## Kept, on the new markup

Each probe keeps its product invariant:

- hoursForecastSmoke - forecast hours. The next stop's one ArrivalEstimate
  (Cycle short, booked window on the ETA's value column), the next-load
  stop card, Dispatch's board and workspace: retained through pending and
  held polls across ValidUntil, replaced without an empty render. Loading
  geometry is now measured by zones of the panel: the head never moves; the
  facts and clocks move only by the next stop arriving (at most its box and
  margins) and keep their shape; a fact cell keeps its height as its value
  arrives. Map element, bounds and native host survive every interaction;
  reselection, Follow, background clicks and Back to truck reread nothing.
- fuelEditorSmoke - the editor opens from the map tool bar: Fuel shows the
  plan card, whose Edit plan opens the editor in the card's place and
  width. Draft, preview, drag (mouse and touch), keyboard order, quantity,
  Full tank, validation, save, reset, cancel, Escape, stale callbacks and
  camera checks are kept.
- mobileTruckScrolling - phone scrolling of the truck panel: closed and
  open, portrait, 600px tall and 200% text; within its cap, vertical only,
  last clock reachable.
- truckReadingsLayout - the readings layout: eight facts in two columns,
  one square icon each, location last, four text clocks in one or two
  rows, 320-767px at 100% and 200%.

The helpers now take the caller's `check`, so one run reports every broken
variant instead of stopping at the first; without it they still throw.

## Removed, and why

The owner removed these on September 26-27; there is nothing left to check:

- the route groups (`fleet-map-route-info__visit/next/where/facts`), the
  Remaining load metric and its final-stop removal, the facility/street/
  town address block with its copy control and ellipsis rule, the stop
  kind in the head (`heading-to`), the load link and the load/order line
  (`mobile-summary__remaining`), the phone's remaining distance row
  (`mobile-summary__distance`), the head's clocks row and duty line
  (`fleet-map-inspector__hours`, `__duty`), the vehicle line of readings
  (`truck-info`, `truck-readings`) and its icon and baseline rules, the GPS
  location line (`TruckLocationLine`), the actions inside the card;
- the card as a centred, width-capped overlay with the shared shadow and
  radius (docked, the panel is a column of the workspace);
- the map key (the owner, September 27: the map carries no key);
- the Dispatch board card's Next recap (checked absent instead);
- the fuel editor's two-column desktop card, its bottom inset and width
  band, the phone's Route / Fuel details / Map tabs and the FuelGauge
  rings (the owner, September 26); the gauge percentages are now read
  from the tank levels in words;
- the desktop rule that the whole route shows without scrolling: the
  editor has the plan card's height and one scrolling list; every row
  must be reachable instead.

Relocated: the toolbar heading/search/layer-chip line checks belong to
`mapToolbarSmoke.mjs`; the layers are the tool bar's Layers menu wider than
a phone and the Filters drawer on it.

Adjusted to new contracts: the recorded fixtures carry the server's
`workPhase` (current / next) and a fuel visit's `visitKey`; the map stubs
answer the map handle's current methods; the fuel fixture answers the
shell's reads and the chain's board read; after Cancel, Save or Calculate
the editor returns to the plan card, not the truck.

## Findings in the product (not fixed here)

Each keeps its probe red on purpose; the owner decides the fix.

- Phone truck panel, 390px: the unit (11006) is clipped and lies under
  the Details button (`_narrow` and `_workspace` disagree on the head).
- Phone panel at 200% text: the head's controls reach 250px past a 310px
  panel, so the panel scrolls sideways and its parts clip.
- Fact labels (`dt`, spaced capitals) overflow their cells at 320px/100%
  by 5px and at 200% below 600px by up to 112px.
- 900px at 200% text: the head's eyebrow and unit clip.
- Phone map tool bar buttons are 44×32px; docs/ui-controls.md asks 44px
  touch targets below 800px.
- Fuel plan editor on phones (390×844, 390×667, 320×667, both themes): the
  list is not bounded by the editor, which clips at its 55% cap; the
  footer (Calculate automatically, Cancel, Save plan) is not visible and
  the rows cannot be scrolled into view. The probe stops at "route row 1
  is fully reachable and uncovered"; the later phone steps are unverified.
- Fuel probe 1440 dark: at 390px/200% one HOS clock reading ("3:34") is
  clipped; light passes. Later steps of that case are unverified.
- Dispatch load workspace at 1200px, both themes: the ETA cell's row is
  4px wider than its column (`stop-workspace__list`).
- The docked empty state ("No truck selected") never shows:
  `_stage.scss` hides an unselected inspector over `_workspace.scss`'s
  rule that draws it; the Razor comment says it should show.

## Runs

Scratch publish `scratch-GK1cai` (Release, `44da6099` + ignored
`wwwroot/appsettings.json`). Pinned runs carry `.keep`.

- hoursForecastSmoke, default matrix of 12: GO0odU. 2344, 1920 and 1440 in
  both themes clean; 1200, 900 and 390 fail only on the findings above. No
  browser errors or unexpected requests.
- hoursForecastSmoke, lifecycle mode (16 transitions): 5L7ZJS, pass.
- An earlier matrix run at 1200-light, made while other probes ran at the
  same time, once saw the Dispatch board render zero forecast cards during
  a held poll (dOPrq3). It did not recur alone (O6uwNZ) or in GO0odU;
  recorded as unexplained, not fixed.
- fuelEditorSmoke, full matrix pSPhva: 1440×1000 light passes, 1440 dark
  stops at the clock finding; each phone case run alone stops at the
  editor finding (rWm6MD has the phone screenshots). The row-reachability
  rule was then relaxed for an opened row taller than the list; 1440 light
  was not rerun after that change.

Not run: PostgreSQL, the .NET suite and `npm test` (browser scripts only),
production traffic. No deployment.
