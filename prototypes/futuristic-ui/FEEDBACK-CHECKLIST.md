# Owner feedback checklist - workspace redesign

Every design request from the owner (September 27, 2026), latest override
first where two conflict. Status: **Built** (in the code on :5180),
**Seen** (inspected on a rendered screen, which theme), **Open** (not done).
Tests are not run during design by the owner's instruction; "Seen" is
visual inspection only, never acceptance.

## Frame and shell

| Request | Status |
| --- | --- |
| Sole interface; old design only in Git (`ui-legacy-2026-09-27`) | Built |
| Top bar: brand, section / page, theme, clock, account | Built, Seen dark/light |
| Rail of destinations, glass panels with corner ticks, canvas glows | Built, Seen |
| Dark is the default theme; no white flash at start | Built (client start); server "never chosen" is Dispatch's |
| Theme switch applies without a page reload, keeping camera, Follow, selection | Open (still reloads the Fleet map page) |
| Phone shell (pulse top bar, bottom nav) | Open (mobile pass later) |

## Fleet head, list, filters

| Request | Status |
| --- | --- |
| Head: title and purpose, search, driver groups, All / Moving / Stopped chips | Built, Seen |
| List columns Truck / Trailer / Driver; trailer number only | Built, Seen |
| Chips and search filter the list AND the map alike; excluded selection released | Built |

## Map

| Request | Status |
| --- | --- |
| Real Google map, mainland US / Canada bounds, no world zoom | Built, Seen |
| Auto satellite at close zoom, no mode caption | Built |
| One compact tool bar: Follow, Fit route, Camera, Route options, Fuel, Layers; no zoom or whole-fleet | Built, Seen |
| Layers menu: Fuel stations, Traffic, Next loads, Map animation (visible on/off) | Built |
| Map animation off stops all decorative motion, keeps static highlights | Built |
| No map legend; weather attribution kept | Built, Seen |
| Truck marks, dark map: the approved earlier marks (green arrow moving, green / grey circle standing), compact size; selected sonar | Built (correction: the engine-edge redesign was asked for the light map only) |
| Truck marks, light map: neutral body, arrow moving, circle standing, engine edge | Built |
| Truck number and "N trucks" cluster tags: HUD plates (clipped corners, fine accent outline, edge ticks, glass sheen), both themes; icons, counts, clustering and picking unchanged | Built |
| Sonar: four thin compact rings, slow, brighter ink on light map | Built, Seen dark (light ink not yet seen) |
| Stop badges: glass core, fine rim, crisp P / D, dashed when done; light variant | Built, Seen dark and light |
| Fuel stations, both maps: pump on the glass core in a fine price-colour rim (dark: cyan ink, as the owner praised; light: its own deep-accent variant); plan stop same pump slightly larger, "Fuel N" | Built (correction: the dark pump is kept; only the truck marks went back to the approved dark look) |
| Selected route glows (current or picked later load), slow breathing | Built, Seen (picked later load) |
| Unselected later routes: fine dashes, no glow; travelled part thin and quiet | Built (not yet seen) |
| Selected P / D / fuel: HUD reticle with four ticks and soft glow | Built |
| Subtle forward direction motion along the selected route (chevrons, still when animation is off) | Built |
| P / D pressed on the map or in the chain (current and later loads): that stop centred at zoom 15, satellite by the zoom rule; Follow ends | Built |
| Passed (completed) stops openable from chain and map | Open |

## Right panel

| Request | Status |
| --- | --- |
| Reserved column; empty state with calm radar when nothing is chosen | Built, Seen |
| Truck head: eyebrow, large unit; no Route / Fuel tabs | Built, Seen |
| Facts 2x3, clocks in cells with bars; no action row; no trip block | Built, Seen |
| Location: locality only, full address in title and copy; copy button, honest status | Built, Seen |
| Stop card (current and next): HUD glass, corner brackets, dimensional emblem, compact facts | Built, Seen dark current / light next |
| Load number opens the load; no large Open load button | Built, Seen |
| Stop card stacks in the narrow panel; no mid-word breaks | Built, Seen |
| Stop the server completed (IsCompleted, not GPS or load state), current and next cards: no ETA, Left, Fuel on arrival, cycle or late warnings; Completed status, completion time only when recorded; address, load link, appointment kept | Built |

## Trip chain

| Request | Status |
| --- | --- |
| No heading column; small fixed height; overflow scrolls inside; wheel scrolls sideways; themed bar | Built, Seen |
| Compact content-width cards, centred, left-aligned group | Built, Seen |
| P / D beside their cities, checked when the server says done | Built, Seen |
| P / D opens that exact stop and zooms; the rest of the card fits the whole trip | Built, Seen (current and next) |
| Status in its own slot, load link separate | Built, Seen |

## Deferred by the owner

- Tests, harness, mobile checks: only when the owner asks.
- Dispatch page redesign to the concept.
