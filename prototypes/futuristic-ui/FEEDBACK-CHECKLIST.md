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
| Theme switch applies without a page reload: the map is made again in place (Google fixes its scheme at creation), keeping camera, chosen truck, Follow, list, chain and panel | Built, Seen dark→light |
| Theme switch is smooth: the page cross-fades its colours; the last map stays under the new one until it has drawn, then fades out | Built |
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
| Layers menu: Fuel stations, Traffic, Next loads; no Map animation option | Built |
| Map animation always on (sonar, route glow, direction marks, radar), also under the system's reduced motion | Built |
| No map legend; weather attribution kept | Built, Seen |
| Truck marks, dark map: engine-edge marks as HUD instruments (glass body lit from the nose, cyan halo instead of white casing, cyan spine / core) | Built (correction: the engine-edge design belongs to dark, not light) |
| Truck marks, light map: classic green arrow moving, green / grey circle standing | Built |
| Truck number and "N trucks" cluster tags: HUD plates (clipped corners, fine accent outline, edge ticks, glass sheen), both themes; icons, counts, clustering and picking unchanged | Built |
| Sonar: four thin compact rings, slow, brighter ink on light map | Built, Seen dark (light ink not yet seen) |
| Stop badges: glass core, fine rim, crisp P / D, dashed when done; light variant | Built, Seen dark and light |
| Fuel stations: no pump icons. Dark map: glass-core dot in a fine price-colour rim. Light map: dot filled with its price colour in a white rim, so cheap and dear read apart; plan stop slightly larger with the accent ring, "Fuel N" | Built |
| Every road ahead glows (current and each later load), the chosen one a little brighter, slow breathing | Built |
| Unselected later routes: fine dashes with the softer glow | Built (not yet seen) |
| Travelled road as a HUD trace: fine instrument-ink line over a faint halo, no white casing; empty miles dashed | Built |
| Selected P / D / fuel: HUD reticle with four ticks and soft glow | Built |
| Direction motion: now and then one light pulse (bright head, fading tail, soft glow) runs along the chosen road's part in view from the truck's side towards its goal, then rests (4.2 s cycle); other roads only glow. Arrows and river streaks were rejected | Built |
| P / D on the map or in the chain (current and later loads): one press opens it, zoom kept (an off-screen stop is panned into view); a double press centres it at zoom 15, satellite by the zoom rule; Follow ends on a camera move | Built |
| Passed (completed) stops openable from chain and map | Open |

## Right panel

| Request | Status |
| --- | --- |
| Reserved column; empty state with calm radar when nothing is chosen | Built, Seen |
| Truck head: eyebrow, large unit; no Route / Fuel tabs | Built, Seen |
| Facts 2x3, clocks in cells with bars; no action row; no trip block | Built, Seen |
| Location: locality only, full address in title; clicking the text copies it (no copy button); honest status | Built |
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
