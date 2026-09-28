# Fleet workspace: feature parity checklist

Updated 2026-09-27 for the Blazor integration on branch
`claude/futuristic-pulsar-ui-concept-80ccd4`, merged with the current-work
candidate `0e6add5d` (server `WorkPhase`). By the owner's decision the
workspace is the only interface: the Current / Futuristic switch and the
earlier layout were removed. The earlier design is kept only in Git, at tag
`ui-legacy-2026-09-27` and branch `recovery/pre-futuristic-ui-2026-09-27`
(both `0e6add5d`). The localhost concept in this folder remains a design
reference only.

"Owner" is the existing implementation the workspace uses.
Status: **Kept**: same owner, unchanged behaviour. **Verified**: exercised
by `Client/tests/browser/workspaceSmoke.mjs`
(compiled Client, real basemap, synthetic read-only APIs). **New**: layout
added by the workspace. **Not checked**: kept by construction but
not exercised in a browser in this pass.

## Fleet Map

| Capability | Owner | Status |
| --- | --- | --- |
| Truck positions, 10 s polling, hidden-tab pause | `FleetMap.razor.cs`, `RefreshLoop` | Kept, Verified (moving truck) |
| Google basemap, light/dark scheme | `Scripts/fleetMap` | Kept, Verified |
| Arrow markers, labels, selected rings, clustering | `truckLayer.ts` | Kept, Verified (render) |
| **Follow selected truck** | `truckCamera.ts` | Kept, Verified: stays on through position updates |
| Satellite at close zoom, road map when zoomed out | `fleetMap.ts` idle policy | Kept, Verified: Follow gives hybrid, zoom-out gives roadmap, Follow again gives hybrid |
| Follow ends on a reader's zoom or drag | `truckCamera.ts` | Kept, Verified (zoom) |
| Fit route, Fuel, Camera, Route options | inspector actions | Kept; Fuel, Camera and Route options Verified at 1440/390/360 px, both themes (layout only; nothing saved or sent) |
| Truck search, driver groups, layer chips | toolbar | Kept, Verified (render) |
| Truck inspector content | `FleetMap.razor` | Kept; docked, the card is open whole (its narrow-container rule), so the Details button is not shown - Verified |
| Next-load stop card | `NextLoadDetailsCard`, `selectNextStop` | Kept: a later trip's stop opens it above that trip (distance, fresh ETA, late-reply guards); Follow in the head; Verified (opened from the chain) |
| Camera kept on return | tab `sessionStorage` per user, same truck only | Changed: no `view` in addresses; Verified by `returnNavigationSmoke` (older link cleaned without a history entry, Back to map and browser Back restore) |
| Fuel plan, fuel editor, send plan | `FleetFuelPlan`, `FuelPlanEditor`, `FuelSendPlan` | Kept; editor in the inspector column on desktop, half the stage on phones; Verified (open, bounds); save and send not pressed |
| Fuel stations layer and quotes | `FleetStationLayer` | Kept, Not checked |
| Route editor | `RouteEditor` | Kept, over the map; frame Verified; its preview request was refused in the harness, so options content was not drawn |
| Weather, map key | `TruckWeather`, key | Kept, Verified (render) |
| Return navigation, playback, historical date | page owners | Kept, Not checked |
| Fleet list (left) | `FleetTruckList` over the page's own search | New, Verified |
| Trip chain (bottom) | `FleetTripChain` over the truck's Dispatch board row (server `WorkPhase`) | New, Verified: same phase words as Dispatch, stale load says Needs refresh, one board read per truck chosen |

## Dispatch

| Capability | Owner | Status |
| --- | --- | --- |
| Cards, Papers, Table | `DispatchList` and views | Kept, Verified: each keeps search and scope |
| Active / Completed | `DispatchList` (Completed in Table only, per candidate) | Kept, Verified |
| Search, driver groups, paging | `DispatchList` | Kept, Verified (search) |
| Phases and conflicts | server `WorkPhase` / `WorkConflict` via `DispatchWorkPhase` | Kept; Fleet chain uses the same source - Verified |
| Board, telemetry, enrichment, planning, HOS polling | `DispatchList` | Kept |
| New load, load workspace, stop editors, assignment | existing pages and editors | Kept, Not checked |
| Locate on map | lane link | Kept |

## Shared

| Capability | Status |
| --- | --- |
| Theme saved to the account | Kept |
| Interface choice | Removed (sole interface); earlier design at `ui-legacy-2026-09-27` |
| Contrast of text roles in both themes | Checked by `styleTokens.test.js` |
| No page overflow at 390 and 360 px, both themes | Verified |

## Known differences and gaps

- Docked, the truck card has no Details / Hide details button: it is open
  whole, as the card already is below `map-compact-columns`.
- The chain shows a later load's stop card only while its road is drawn
  (Next loads on); otherwise the load links to its workspace.
- The page shell does not yet match the approved demo (topbar, page head
  with chips, fleet table, Route/Fuel tabs); that rebuild follows.
- The existing `fuelEditorSmoke` and `routeEditorSmoke` fail on the
  untouched candidate `0e6add5d` at their first card-layout assertion
  (probe rot from the September 26-27 card changes), so they could not
  serve as evidence; `workspaceSmoke` covers the editors'
  layout instead. Repairing those probes belongs with the card owner.
- Live data: renders use synthetic read-only fixtures; no signed-in check.
- Release: not deployed. Integration goes through the Dispatch release owner
  after its frozen release.
