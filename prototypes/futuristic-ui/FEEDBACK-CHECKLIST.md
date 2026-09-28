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
| Light theme in the same class as dark: steel canvas with a fine instrument grid; clean white glass panels (no map tint) with a cyan rim, bold ticks and cool shadow; Google's light road map quieted to cool steel (satellite untouched, trucks and routes keep their colours); later trip cards a solid rim with a colour bar | Built, Seen light 1100 px |
| Truck panel as tall as its content; the map shows beneath it | Built, Seen |
| Light theme roads: a finer crisp line of the trip's colour in a faint haze, no white casing; only the chosen road glows, softly; the flow grains carry no glow. Light basemap keeps Google's colours, only slightly desaturated (grey and dimmed versions were rejected) | Built |
| Light chain: trips strung on a fine line through filled step tokens; white tiles with a straight colour line along the top (clear of the corners), soft-tint phase chips; chosen trip rimmed in its colour, softly lit and lifted | Built |
| Light emblems (truck, stop P / D, pump): a lit accent disc with a white glyph; a passed stop stays dashed | Built |
| Fleet list as tall as its trucks; weather credit a quiet line, not a plate | Built |
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
| Real Google map: the camera's centre kept to mainland US / Canada; zooms out to the whole continent (min zoom 3) so a coast-to-coast load fits in the free part, never the world | Built |
| Auto satellite at close zoom, no mode caption | Built |
| One compact tool bar: Follow, Fit route, Camera, Route options, Fuel, Layers; no zoom or whole-fleet ; always whole - the truck actions dimmed and disabled with no truck chosen; centred across the map | Built, Seen |
| Layers menu: Fuel stations, Traffic, Next loads; no Map animation option ; closes on a press anywhere outside it | Built |
| Map animation always on (sonar, route glow, direction marks, radar), also under the system's reduced motion | Built |
| No map legend; weather attribution kept | Built, Seen |
| Truck marks, dark map: coloured glass by engine - green running, bright cyan off, grey with a dashed edge unknown - in a dark casing with a glow of the same colour; moving an arrow, standing a HUD sight (bold ring around the glass core); one size (24 px) | Built |
| Truck marks, light map: classic green arrow moving, green / grey circle standing | Built |
| Empty miles as HUD: fine dashed amber line over a faint amber halo, no white casing | Built |
| Clicking a truck cluster glides the camera (0.7 s ease) instead of jumping | Built |
| Truck number and "N trucks" cluster tags: HUD plates (clipped corners, fine accent outline, edge ticks, glass sheen), both themes; icons, counts, clustering and picking unchanged | Built |
| Sonar: four thin compact rings, slow, brighter ink on light map | Built, Seen dark (light ink not yet seen) |
| Stop badges: glass core, fine rim, crisp P / D, dashed when done; light variant | Built, Seen dark and light |
| Fuel stations: no pump icons. Dark map: glass-core dot in a fine price-colour rim. Light map: dot filled with its price colour in a white rim, so cheap and dear read apart; plan stop slightly larger with the accent ring, "Fuel N" | Built |
| Every road ahead glows (current and each later load), the chosen one a little brighter, slow breathing | Built |
| Unselected later routes: fine dashes with the softer glow | Built (not yet seen) |
| Travelled road as a HUD trace: fine instrument-ink line over a faint halo, no white casing; empty miles dashed | Built |
| Selected P / D / fuel: HUD reticle with four ticks and soft glow | Built |
| Direction motion, constant and calm, like current: many fine grains (13 px apart, 2 px heads, short tails, about 10 px/s) over a fine steady glowing wire, on the current and later roads, brightest on the chosen one | Built |
| P / D in the chain (current and later loads): one press opens the stop and shows its whole trip; a double press centres the stop in the free part at zoom 15. P / D on the map: one press opens it (a hidden stop brought into the free part), a double press zooms to 15. Satellite by the zoom rule; Follow ends on a camera move | Built |
| Passed (completed) stops openable from chain and map | Open |

## Workspace surface

| Request | Status |
| --- | --- |
| Map lies under the whole workspace; list, panel and chain float over it as tempered glass (thinner, strongly blurred, lit top edge); the camera keeps fits and reveals clear of them and of the map's tool bar | Built, Seen dark 1440 px |
| Panel head reaches the panel's right edge (no scrollbar lane); inner blocks stay solid (translucent inner blocks were rejected) | Built |

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
| Stop card assignment on one line: Truck · Trailer · Driver (HUD labels in the next card) | Built |
| Stop the server completed (IsCompleted, not GPS or load state), current and next cards: no ETA, Left, Fuel on arrival, cycle or late warnings; Completed status, completion time only when recorded; address, load link, appointment kept | Built |
| Truck panel, station card and fuel plan drawn as the stop cards: glass emblem (truck / pump), lit frame with corner brackets, HUD labels, accent-edged clocks and visit numbers | Built |
| Station price days: Today marked as an instrument (faint accent tint, fine rim, lit brackets, price in the accent) instead of a solid block; HUD labels, each day centred in its equal column so the gaps around Today are equal | Built |
| Docked panel fills its column to the right edge in every mode (truck, stop, next stop, station) - the floating card widths no longer apply | Built |
| Fleet list truck numbers one step smaller (body size, semibold) | Built |
| No Keyboard shortcuts link on the map; Google's data credit and Terms stay (required by Google's terms) | Built |

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
