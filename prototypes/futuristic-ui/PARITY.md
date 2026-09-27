# Futuristic interface: feature parity checklist

Status on 2026-09-27. "Concept" is the localhost prototype in this folder.
"Integration" is how the capability reaches production: the existing Blazor
page and its owners stay; the Futuristic interface changes layout and styling
only, behind the current interface as a fallback. Nothing here is a second
implementation of business logic.

Legend: **Yes** works in the concept on live and demo data; **Partial**
visible but reduced; **No** not in the concept; the production owner keeps it.

## Fleet Map

| Capability | Current owner | Concept | Integration |
| --- | --- | --- | --- |
| Truck positions, 10 s refresh, paused when hidden | `FleetMap.razor.cs`, `RefreshLoop` | Yes (same endpoint, cadence, visibility pause) | Keep owner |
| Google basemap, dark/light scheme | `Scripts/fleetMap` | No (vector boundaries only) | Keep Google map; restyle chrome |
| Compact arrow markers with unit labels | `truckLayer.ts` | Yes | Restyle marker SVG only |
| Selected truck rings | `truckLayer.ts` | Yes | Restyle only |
| Nearby truck clustering with counted groups | `truckLayer.ts` | No | Keep owner |
| Label collision and connectors | `truckLayer.ts` | No (cities avoid markers only) | Keep owner |
| **Follow selected truck** | `truckCamera.ts` | Yes: anchor kept across position frames (0 px drift); ends on drag, zoom, selection change, Fit route, close | Keep owner; button moves into the new panel |
| Fit route / Show route | `FleetMap.razor.cs` | Yes (ends Follow) | Keep owner |
| Truck search (truck, driver, trailer) | FleetMap toolbar | Yes, plus load numbers | Keep owner; restyle |
| Driver group picker | `ChosenDriverGroup` | No | Keep owner (server-side filter) |
| Layer chips: Fuel stations, Traffic, Next loads | FleetMap toolbar | Partial: future routes, other trucks, city labels | Keep owners |
| Fleet list panel (left) | none today | Yes (new layout, same data) | New layout component, reads existing feeds |
| Truck card: unit, trailer, driver, trailer conflict | inspector | Yes | Reuse inspector content |
| Left-to-next-stop, ETA, Appointment in zone | inspector | Partial (ETA from planning, appointment as sent) | Reuse `StopAppointmentDisplay`, arrival components |
| Speed, fuel %, engine, temperature | inspector | Partial (no temperature) | Reuse |
| HOS clocks (Break rules for Canada) | `DriverHours` | Partial (four clocks, no jurisdiction rule) | Reuse `DriverHours` |
| Duty summary and rest build-up | `DriverDutySummary` | Partial (duty label only) | Reuse |
| GPS address copy | inspector | No | Reuse |
| Current route + next loads with deadhead | `MapRoutePublisher`, next-routes | Yes | Keep owners |
| Off route / GPS stale / notices | planning result | Yes | Reuse |
| Trip list with stops (right) and trip chain (bottom) | none today | Yes | New layout components over board/planning reads |
| Stop inspector / Back to truck | inspector | Partial (stop highlight, no inspector) | Reuse inspector |
| Fuel plan read | `FleetFuelPlan` | Yes (read only) | Reuse |
| Fuel plan editor, Recalculate, Send fuel plan | `FuelPlanEditor`, `FuelSendPlan` | No (disabled, read-only concept) | Reuse; must work in the new layout |
| Fuel stations layer and station card | `FleetStationLayer` | No | Reuse |
| Camera | `TruckCamera` | No (route blocked by the concept host) | Reuse |
| Route options / route editor | `RouteEditor` | No | Reuse |
| Next-load details card | `NextLoadDetailsCard` | Partial (trip card) | Reuse |
| Weather with attribution | `TruckWeather` | No | Reuse |
| Map key | FleetMap | Yes (legend in chain) | Restyle |
| Historical date / playback | playback modules | No | Keep owner |
| Mobile: filters sheet, details toggle | FleetMap | Yes (sheets, trip strip) | New layout; same owners |

## Dispatch

| Capability | Current owner | Concept | Integration |
| --- | --- | --- | --- |
| Board projection, 12 per page, 1 min refresh | `DispatchList.razor.cs` | Yes (same endpoint and page size) | Keep owner |
| Telemetry 10 s, HOS 15 s, planning summaries 10 s/1 min | `DispatchList` | Yes (locations feed instead of telemetry) | Keep owners |
| Enrichment (ETA, financials) | `DispatchList.Enrichment.cs` | No | Keep owner |
| Search (truck, driver, trailer, load) | toolbar | Yes | Keep owner |
| Cards / Table / Papers views | `DispatchList`, `DispatchTable` | Partial (cards-style lanes only) | Keep all three |
| Active / Completed scope | toolbar | No | Keep owner |
| Driver group picker | `ChosenDriverGroup` | No | Keep owner |
| Truck lane: truck, driver, trailer, motion, HOS | `DispatchRig`, `DriverHours` | Yes | Reuse components |
| Load card: phase, status, stops, appointment, ETA | `DispatchLoadCard` | Yes | Reuse; restyle |
| Order copy, customer, rate | `DispatchLoadCard` | Partial (no copy) | Reuse |
| Locate on map | lane link | Yes (keeps selection) | Keep |
| Open load / load workspace | `DispatchStopWorkspace` | No (disabled) | Reuse |
| New load | header | No (disabled) | Reuse |
| Truck assignment, stop operation, completion, correction | `TruckAssignmentEditor` and editors | No (blocked, read-only) | Reuse |
| Unassigned loads | board rows without truck | Yes (queue) | New queue layout over the same rows |
| Attention list | none today (facts scattered) | Yes (server facts only) | New layout; no new rules |
| Pagination | `DispatchList` | Yes | Keep |

## Shared

| Capability | Concept | Note |
| --- | --- | --- |
| Normal sign-in, token refresh, sign-out | Yes | Existing auth endpoints through the loopback host |
| Light/dark | Yes | Production keeps the saved account preference |
| Live / stale / loading / unavailable states | Yes | Per feed, with last-good time |
| Demo fixtures | Yes | Only when explicitly chosen; labelled everywhere |
| Keyboard: search `/`, arrow list/results, map arrows and +/- | Yes | |
| Phone 390/360 without page overflow | Yes | Checked by `check.mjs` |

## Not release-ready yet

The concept is a design and data-agreement reference. Release requires the
integration column above: the Futuristic layout built into the existing Blazor
pages behind a fallback to the current interface, every **No** row working
through its existing owner, and the owner-run release through the Dispatch
deploy owner.
