# Historical route planning

This is the earlier guide, retained as evidence. Some descriptions below are
superseded. Use the [current guide](../../features/route-planning.md) for operating rules.

# Automatic truck routes and fuel planning

Click a truck on Fleet Map or use its map link in Dispatch. The server selects the first current/upcoming load using the same ordering as the Dispatch board, builds its truck route, and highlights up to three BVD fuel options. Dispatch displays the same route mileage, current fuel reading and station recommendations without a form or Calculate button. Load details automatically use that specific load.

Routes use existing stop coordinates and addresses in their stored order. The fleet uses standard 53-foot trailers automatically. The routing profile keeps 72 feet as the estimated combined tractor/trailer length, 13 ft 6 in height, 8 ft 6 in width, 80,000 lb and five axles. [TomTom vehicleLength](https://docs.tomtom.com/routing-api/documentation/tomtom-maps/v1/common-routing-parameters) includes the whole vehicle and attached trailer, so it must not be set to the trailer length alone. Dispatch and Fleet Map have no per-load settings or confirmation form; fleet preferences are available separately at `/settings`. Tank capacity and MPG are never fabricated. The initial route provides the original planned mileage; live GPS and dispatch stops determine the next stop automatically. Passed pickups are excluded from the remaining route even when the import has not yet reported pickup completion.

The map shows the full route, grey completed distance and numbered load stops. Recommended station circles get a blue outline and remain visible with the general Fuel Stations layer off. Selecting another truck replaces the route and highlights; opening a station keeps that route. Only an explicit selection fits the route; periodic updates preserve the viewport and truck animation.

GPS refresh projects onto the stored route locally. Stop progress is saved in the route plan: reported completion, visits followed by departure, and position/direction beyond due stops advance the next stop in sequence. GPS older than ten minutes and future scheduled stops do not trigger inferred completion. Approaching a pickup from its far side does not count as having completed it. Inferred progress never overwrites imported arrival, pickup or delivery timestamps. Once all stops are passed, truck selection uses the next assigned load.

If the truck leaves the route, the planner routes from GPS through only the remaining stops. Deviations over two miles are handled immediately; smaller deviations over 0.75 miles require distinct GPS observations spanning 45 seconds. Reroutes are limited to once per five minutes and require at least one mile of movement since the last reroute. A pending pickup behind the current projected position also triggers a route to that pickup. Near-stop GPS drift does not trigger rerouting. The reference route and original mileage remain saved; stale fuel options are invalidated whenever the road changes. Passed stop markers disappear without fitting the map again.

 Missing, stale (>10 minutes) or off-route (>0.5 miles) telemetry does not produce a false remaining distance. Original planned miles are retained when route inputs change. Progress measures distance along the planned road, not audited odometer miles.

## Settings

`/settings` is available from the sidebar. Authenticated users can save shared fleet preferences, consistent with access to the planning endpoints: compare after IFTA, maximum fuel detour, minimum reserve and target fill. Cost comparison fields are not exposed; saving or restoring the visible preferences preserves the existing stop cost, driver hourly cost and CAD-to-USD rate. Geometry and the 53-foot trailer profile remain automatic. No tank size or MPG is requested on this page.

Preferences are persisted in `FleetPlanningSettings` with optimistic revision checks to prevent one session from overwriting another. Restore defaults changes the draft; Save settings applies it. Invalid or non-finite values are rejected. Settings affect new and existing truck profiles, and the map refreshes its price basis from the server. All sessions use the same fleet settings.

Changing preferences invalidates fuel results through a settings signature; route geometry and route version are preserved. Cached detour requests may be reused, while newly selected candidates can require a new road check. An in-flight result based on older preferences cannot be stored as current. With IFTA enabled, station comparisons exclude missing IFTA rates instead of mixing price bases. Fuel quantities require actual fleet capacity and MPG as before.

## API and storage

Server scheduling, background route preparation, caching and Cloud Run settings are documented in [SYNCHRONIZATION.md](../../features/synchronization.md). Production scheduling prepares routes without browser requests; local development returns saved plans first and queues on-demand updates in the background.

- `POST /api/fleet/trucks/{truckId}/planning`: automatically select the current/next load and ensure its route and recommendations exist; no request fields required.
- `POST /api/dispatch/{dispatchId}/planning/automatic`: the same automatic workflow for a specific load.
- `GET /api/settings/planning`: shared preferences and revision.
- `PUT /api/settings/planning`: save `preferences` with the last-read `revision`; stale revisions return HTTP 409.
- `GET /api/dispatch/{dispatchId}/planning`: effective truck settings, saved plan and current telemetry progress.
- `PUT .../profile`: save confirmed truck settings.
- `POST .../route`: calculate or reuse a truck route. Body: `profile`, `fromCurrentPosition`, `nextStopSequence`.
- `POST .../fuel`: calculate and save a fuel plan. Body: `profile`, optional `currentGallons` override.

All endpoints require the existing authenticated TMS session. The provider key is server-only: `TomTom:ApiKey` in Development User Secrets; `TomTom__ApiKey` in hosted configuration. Enable Routing for the key; address geocoding uses the separate Google integration. No key is returned in DTOs or written in the request-cache table. HTTP client URI logging is disabled for this provider because TomTom authenticates using a query parameter.

`AddRoutePlanning` creates three additive tables: `TruckPlanningProfiles`, `DispatchRoutePlans`, `RoutingApiCalls`. It has been applied to the configured development connection (the existing Neon database). `AddPlanningSettings` adds the shared `FleetPlanningSettings` table and has also been applied to that connection. Server models live under Application/Features/Routing/Models; client DTOs live under Client/Models/DTO/Planning. The client and Application have no Shared project dependency.

Automatic display reuses the persisted route until routing inputs change or a sustained GPS deviation requires rerouting to the next stop. Opening the page or receiving another GPS point does not expire the saved road geometry. Station recommendations refresh at most every 30 minutes (or on a new price date); repeated refreshes use the routing provider cache. Errors back off for two minutes at the automatic endpoint. No-load trucks make no routing calls.

Identical provider route requests reuse a DB cache for 12 hours. The original provider calculation time stays attached to cached routes. Pending attempts reserve a one-minute retry window; transient provider or malformed-response failures generally back off for five minutes. Request reservations, caching and daily accounting are serialized with PostgreSQL advisory locks across API instances. The configured ceiling is **1000 uncached TomTom routing attempts per UTC calendar day** across the fleet, using `TomTom:DailyRequestLimit`, plus 30 attempts/minute. Successful, failed and interrupted attempts count; cache hits do not. Google address lookups have separate accounting. These are application limits, not assertions about provider billing or quotas. Configure provider account limits separately for activity from other apps/keys. Matrix API is not used.

The separate per-truck policy is temporarily disabled by `RouteRecalculationBudget:Enabled=false`. When enabled, it allows at most 12 reroute attempts in a rolling day and three in a rolling hour, with a 15-minute interval and three-mile movement requirement. Disabling it skips its database reservation, but preserves existing attempt history for re-enabling. The shared TomTom ceiling, cached routes, request deduplication, fresh-GPS and sustained-deviation guards remain active. This does not recalculate future delivery-to-pickup connections merely because the current truck deviates. Restart the API after changing this option; the options type defaults to enabled when configuration is absent.

## Fuel recommendations

Without capacity/MPG, the system shows road-checked route options and prices, not invented purchase quantities or a guarantee that a station is reachable. It includes the nearest station and lower-priced options in the next 300 route miles; with fresh fuel at 25% or below, that window becomes 100 miles. Price comparisons stay within the same currency and volume unit; IFTA prices are compared only when the group has applicable rates. At most three candidate detours are checked. Passed stations are filtered from responses without another paid request.

If stored fleet data already contains capacity/MPG and fresh fuel telemetry is available, the existing quantity optimizer runs automatically and replaces route options with a fuel plan. There is no manual fuel override in the UI.

## Quantity optimizer

The planner uses current BVD diesel discounts and the selected IFTA basis consistently. CAD/L converts to USD/US gallon using the configured USD-per-CAD rate. CAD stations are excluded when that rate is absent. With IFTA comparison enabled, stations without an applicable rate are excluded rather than mixing net and gross prices. These are planning estimates, not an IFTA filing calculation.

Samsara fuel timestamps are preserved independently of GPS timestamps. A missing or >15-minute-old fuel reading prevents automatic quantity recommendations; route options remain available. No assumed tank size or fabricated MPG is used. The selected reserve is enforced at each station and at the destination; consumption is conservatively rounded upward to whole gallons for each leg.

Station candidates come from the local BVD data within two miles of the route ahead of the truck. Up to 12 candidates are shortlisted across the route, including currently reachable stations. Each is checked with truck routing through the station between nearby route points. The detour includes entry and return mileage and estimated extra drive time. Stations over the configured detour time or 10 extra miles are excluded. This is a shortlist search, not a global optimum over every station. If the verified subset cannot complete the trip with reserve, no partial plan is presented as feasible.

A dynamic program selects stations and partial/full fill amounts. Its objective includes comparable fuel cost, a fixed cost per stop and driver time for the detour. Savings compare that complete plan with the cheapest plan using the fewest stops within the same candidate set. Monetary totals are labelled USD; individual source prices retain their original currency/unit. Arrival fuel estimates are conservative.

Fuel plans are marked for refresh when 30 minutes old, the next station was passed, telemetry deviates from the forecast, settings change, or the route changes. Planned fuel markers are hidden when the plan requires refresh. Nothing is sent to drivers.

## Current limits

- Driving time estimates use route calculation traffic; no continuous traffic-only rerouting, HOS, loading time, queues, toll comparison or automatic fuel purchase detection.
- BVD supplies current quotes, not guaranteed prices at future arrival; verify again before purchasing. Station opening hours and pump availability are not present in the current data.
- Exact truck entrances can need review even when a route is returned. An unsupported travel-mode section is rejected. Driver navigation and automatic dispatch messaging are later work.
- Confirm the account's TomTom data retention/display licence before production rollout; this implementation renders the returned route over the existing Google base map with TomTom attribution.

Verification: unit/integration tests cover provider caching and limits, truck parameters and restricted sections, route persistence and GPS-only progress, stale/off-route locations, fuel reserve/capacity, partial fills, detour consumption, stop penalties and savings. A real TomTom test route succeeded and its repeat used the cache. Automatic-mode tests cover empty assignments, stop-level truck assignment, default profile provenance, repeated selections without paid calls, passed stations and failure backoff.
