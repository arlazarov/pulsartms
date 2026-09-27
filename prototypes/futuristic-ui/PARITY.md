# Futuristic interface: feature parity checklist

Updated 2026-09-27 for the Blazor integration on branch
`claude/futuristic-pulsar-ui-concept-80ccd4`. The Futuristic interface is now
a layout and style scope inside the existing Client pages, chosen in Personal
settings, with the current interface as default and fallback. The localhost
concept in this folder remains a design reference only.

"Owner" is the existing implementation the Futuristic interface uses.
Status: **Kept**: same owner, unchanged behaviour. **Verified**: exercised
in the Futuristic interface by `Client/tests/browser/futuristicSmoke.mjs`
(compiled Client, real basemap, synthetic read-only APIs). **New**: layout
added by the Futuristic interface. **Not checked**: kept by construction but
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
| Fit route, Fuel, Camera, Route options | inspector actions | Kept, Not checked in this pass |
| Truck search, driver groups, layer chips | toolbar | Kept, Verified (render) |
| Truck inspector content and details toggle | `FleetMap.razor` | Kept, docked beside the map on wide screens |
| Next-load stop card | `NextLoadDetailsCard`, `selectNextStop` | Kept, Verified (opened from the chain) |
| Fuel plan, fuel editor, send plan | `FleetFuelPlan`, `FuelPlanEditor`, `FuelSendPlan` | Kept; editor placed in the inspector column; Not checked |
| Fuel stations layer and quotes | `FleetStationLayer` | Kept, Not checked |
| Route editor | `RouteEditor` | Kept, placed over the map; Not checked |
| Weather, map key | `TruckWeather`, key | Kept, Verified (render) |
| Return navigation, playback, historical date | page owners | Kept, Not checked |
| Fleet list (left) | `FleetTruckList` over the page's own search | New, Verified |
| Trip chain (bottom) | `FleetTripChain` over current load and next routes | New, Verified |

## Dispatch

| Capability | Owner | Status |
| --- | --- | --- |
| Cards, Table, Papers | `DispatchList` and views | Kept, Verified: each keeps search and scope |
| Active / Completed | `DispatchList` | Kept, Verified |
| Search, driver groups, paging | `DispatchList` | Kept, Verified (search) |
| Phases (Current / Next / Upcoming) and ETA | Dispatch phase resolver, server ETA | Kept, same resolver in all three views |
| Board, telemetry, enrichment, planning, HOS polling | `DispatchList` | Kept |
| New load, load workspace, stop editors, assignment | existing pages and editors | Kept, Not checked |
| Locate on map | lane link | Kept |

## Shared

| Capability | Status |
| --- | --- |
| Theme saved to the account | Kept |
| Interface choice | New: on this device, per account; covered by `AppearanceSettingsTests` |
| Contrast of text roles in both Futuristic themes | Checked by `styleTokens.test.js` |
| No page overflow at 390 px, both interfaces and themes | Verified |

## Known differences and gaps

- The Fleet chain names loads Next and Upcoming by their order in the
  server's next-routes list; Dispatch names them with its own phase resolver.
  They agree on this fixture. They could differ where the board and next
  routes disagree about order. The server-owned work model under
  construction on the Dispatch architecture branch should become the single
  source for both.
- The chain shows next loads only while the Next loads layer is on (it
  offers a button to turn it on), because that layer owns the reads.
- The interface choice is not yet an account preference; it does not follow
  the user to another device.
- Live data: the renders use synthetic read-only fixtures. A signed-in check
  against real data has not been done.
- Release: not deployed. Integration goes through the Dispatch release owner
  after its frozen release.
