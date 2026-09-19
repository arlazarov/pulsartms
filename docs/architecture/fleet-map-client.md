# Fleet Map client ownership

The page coordinates selection, current/next-load identity, polling and rendering.
Its partial files share that component state; they are not separate services.
Do not move methods between partials merely to reduce a file's line count.

Planning cache reset cancels all HTTP work from its previous session lifetime,
including forced refreshes no longer present in the coalescing dictionary.
Per-consumer cancellation still leaves another active consumer's read running.
Disposal clears bounded snapshots and cancels transports; it does not persist
geometry or add a background cleanup timer.

| Owner | Responsibility | Lifetime |
| --- | --- | --- |
| `FleetMap` | Razor state, truck selection, query parameters, polling and coordination of current/next routes | Component |
| `FleetMapSession<T>` | JS module import/retry, map creation, callback reference and deterministic JS teardown | One per component |
| `FleetStationLayer` | Station requests, cancellation, successful date and failure state; publish only the latest request | One per map instance |
| `NextLoadDisplayCache` | Bounded complete snapshots for returning truck selections | Component |
| `FleetMap.NextLoadDetails` | Pinned future-load inspection, cancellable detail reads and bounded detail reuse | Component |
| `FleetMap.Inspector` | One top-panel mode, revisioned native callbacks and current-truck ownership checks | Component |
| `FleetMap.FuelEditor` | Selected-truck editor identity, station callbacks and saved-plan publication | Component |
| `FuelPlanEditor` | Ordered draft, selected-visit server quantity table, cancellable structural previews and explicit save/reset | Open editor |
| `FleetMap.RouteEditor` | Selected-load editing session, guarded map callbacks and scoped cache invalidation | Component |
| `RouteEditor` | Alternative selection, via inputs, cancellable server previews and explicit save | Open editor |
| `MapRoutePublisher` | Version-aware current-route serialization and JS acknowledgement | Component |
| `FleetRouteDisplayMemory` | Same-route ETA and displayed-progress retention during recalculation | Component |
| `PlanningDisplayCache` | Saved planning snapshots and HTTP revision protocol | Existing Client service lifetime |
| `ArrivalDisplayMemory` | Bounded display-only ETA retention for the same stop | Component |
| `PageVisibility` | Browser visibility subscription and wake signal for coordinated polling | Component |

Session startup is shared while in flight. An explicit retry after failure may
create a replacement map; a failed dynamic import has one cache-busting retry.
Disposal waits for in-flight resource acquisition, releases the map and module,
and disposes its callback reference. It never starts API polling or chooses loads.

The provider host retains one map across page navigation, while each mount owns
and disposes its GPU scene. Scene layers stay filtered until Deck has loaded and
the provider has supplied a current camera frame. On vector maps, a transient
empty `WebGLOverlayView` requests that frame through `requestRedraw`; it detaches
after drawing or when the scene is disposed. Deferred callbacks cannot revive a
disposed scene. This handshake does not pan, zoom, refit, allocate another canvas
or start polling. The provider adapter remains the sole camera-transform owner;
production code does not access its private Deck or native-overlay fields.

The truck layer captures the camera viewport's free-region screen offset when
Follow starts. Playback applies that captured offset to each displayed position,
not the latest inspector dimensions. Details/Hide and delayed content still update
cached insets for future explicit focus, without shifting the active Follow
anchor. Actual map resizing or explicit Follow reactivation captures a new offset.
Stopping Follow or disposing the layer releases its captured centering function.

Truck-group clicks stop Follow through the existing truck-layer method before
zooming to the group's geographic center. They retain the selected marker,
inspector, disclosure and current/next routes, without a page deselection callback
or new data requests. Both count labels and anchors consume the map click.

The station layer does not own the selected date or visibility toggles. The page
decides when to request data, reuses the loaded date on visibility changes, and
passes its lifetime cancellation token. New dates, hiding stations and disposal
cancel pending work. Late responses cannot replace newer station data or mark an
obsolete date as loaded. A failure retains the last successful stations/date.

The same owner independently reads the small daily `fuel/price-overview` snapshot
on map initialization and date changes, even with ordinary stations hidden. This
contains station IDs and selected cash/IFTA prices, without addresses, locations
or complete discounts. Its last successful date is cached; date changes and disposal
cancel superseded requests. Hiding ordinary stations does not cancel price colors.
Both planned and ordinary points use the same all-station currency scale; missing
daily comparison data stays neutral rather than ranking only planned purchases.
Detailed station reads replace the same-day overview as the richer price source.

`FleetMap.Preferences` owns four browser-local map preferences, keyed by the
authenticated account ID from the existing cascading authentication state.
It restores once before session initialization without additional API reads.
Defaults remain usable if storage is unavailable or malformed. Explicit changes
save the four booleans; map retries reuse the current selection without restoring
over newer input. No route, date, search, user profile or provider data is stored.

`Next loads` also controls recommended fuel visits. Future loads are hidden by
default unless the account's browser preference enables them. The page applies
that choice before telemetry or routes are published. Each purchase is filtered
by dispatch ownership before grouping
visits at a physical station. Legacy purchases require a matching current stop ID;
unknown ownership is not inferred from mileage. Toggling reuses the saved plan and
latest displayed progress, without recalculation or changing prices, quantities,
visit numbers or the all-assigned-load planning horizon. Ordinary price markers
remain controlled by the separate `Fuel Stations` toggle.
Recommended stations retain their price-colored point and selection ring, with a
compact dark `Fuel 1` order badge above them. Repeat visits share one badge without
renumbering. Fuel badges are distinct from the circular load-stop numbers and remain
clickable; price, purchase quantity and arrival/departure fuel stay in the card.

Station cards offer add/edit actions for the selected truck's current plan; repeat
visits can be added independently. The editor keeps a frozen snapshot timestamp
until dismissed and never merges polling responses into the draft. Truck/current
dispatch changes discard it. The server prepares five-gallon slider choices for
the selected stop, including downstream redistribution, balances, costs and errors.
Client copies those values without an HTTP request or financial calculations.
Station/order changes and selecting another visit request fresh choices; the
matching table must be ready before quantity controls are enabled. Responses
from an older API without choices retain the debounced preview compatibility path.
Unresolved rows retain identity and removal controls without invented gauges.
Save/reset callbacks recheck truck and dispatch ownership before publishing.
Calculate automatically directly requests a replacement with the opened version
token, including manual plans and edited drafts. No second confirmation is shown;
the busy guard prevents duplicate submission and failures retain the draft.

Dispatch Cards reads one bounded planning-summary batch for the visible page,
not geometry-bearing plans per card. Its page-scoped summary store is separate
from the map's complete-geometry cache. Dispatch telemetry requests only visible
truck status fields; it does not download map coordinates or playback history.
Unchanged status responses do not trigger a whole-board render. Stop edits
invalidate only affected truck/load cache entries. Fleet Map and Dispatch pause
periodic HTTP work while hidden and wake their existing coordinated loops when
visible; no additional polling loop is started on resume.

The Calculate Fuel control captures its dispatch and request ownership before
notifying the page. Switching dispatches cancels that ownership, including A→B→A;
late replies cannot publish a plan, report a failure, or clear a newer operation's
busy state. A response must also match the requested dispatch. Cancellation does
not guarantee that a server-side calculation already in progress was rolled back.
Publishing saved or recalculated fuel supersedes only the matching planning-cache
keys and their pending refreshes. Both truck and dispatch keys receive the result,
even on a cold cache; unrelated truck previews and refreshes remain eligible.

One information inspector is centered horizontally with an `md` top gap capped
by its actual side clearance. The top gap disappears when the inspector fills
the map width. Its named maximum width leaves the map visible on both sides on
spacious screens; narrower maps use the available width. Data remains compact and left-aligned.
Truck, current stop, fuel station and future stop clicks switch its contents rather
than opening separate lower cards. Its absolute placement never changes the map
element or its bounds. Truck information and the current route retain their
mounted display components while another mode is visible. A cold route read uses
the same mileage, address and ETA slots as the ready view, without a lifecycle
message. The bounded panel scrolls internally; mobile truck information keeps its
summary toggle. Truck inspection starts with driver, fuel, route controls and a
compact remaining-distance/next-stop/ETA row. Details expands the same mounted
components on every screen size without requesting another calculation.
No selection reserves no map space. Empty placeholders must not
retain an invalidated route, ETA or ownership identity.

JavaScript exclusively owns the persistent `.fleet-map-inspector__native` subtree,
reusing the existing current-stop and station HTML renderers. Explicit marker
activation selects its owner; polling cannot activate an inactive renderer or
reopen dismissed content. Each intentional transition has a monotonic revision.
The page rejects older callbacks and callbacks for another selected truck. Back
and Escape from stop/fuel details return to the selected truck. Close or Escape
on the truck card uses the page's full deselection flow, cancelling pending reads
and clearing route/follow state. Closing stop/fuel details hides only the inspector.
Blank-map clicks collapse truck Details without deselecting, or return a
stop/fuel inspector to its truck. Back replaces Close when a truck is available.
Marker clicks still select new content. A truck click retains its road without
fitting it. Show route beside Follow explicitly fits the retained remaining
geometry, turns Follow off and makes no HTTP request. Polling does not refit the
camera. Overview zooms group nearby
unselected trucks in stable screen-distance buckets; selected trucks remain
individual. Clicking a group zooms into its approximate center. Group labels sit
directly at that center, without collision displacement. Nearby station and stop
updates do not rebuild truck groups. Zoom changes retain station and road layer
caches; individual truck label spacing also responds to fractional zoom.
Future routes use a thinner dashed stroke until selected.
The fuel editor remains a separate explicit editing action on the same map.
The page suspends inspector ownership while fuel/route editing or the camera is
open, retaining the mounted truck data but hiding its shell. Native marker callbacks
cannot reacquire it while suspended; delayed Blazor callbacks are rejected too.
Closing resumes the selected truck inspector without requesting a route fit.
Camera content is mounted beside the inspector, not inside its hidden subtree.
The route editor is mutually exclusive with fuel editing. Its session ID rejects
callbacks from older editors; polling cannot replace the preview. Ordinary scene
objects remain mounted but hidden while preview roads are visible. The small
transient draggable via/road handles use native Advanced Markers, while preview
roads and numbered load stops use the shared GPU scene. Closing releases their
listeners and objects and restores the normal scene.

Route identity and selection-version guards remain in the page. This extraction
does not change endpoint contracts, route calculations, geometry revisions,
Next Loads cache limits, polling cadence or ETA freshness.

Current geometry and progress enter the route layer together. A cold saved preview
without finite progress draws the saved road and fulfills an explicit fit once.
The first valid progress trims it to the remaining section without moving the
camera again. This visual fallback does not publish measured progress. Missing
progress on the same warm geometry preserves its already-trimmed road without
reporting retained mileage as a new measurement. Truck, dispatch or geometry
changes clear the draw position. Manual dragging, following and disposal cancel
pending route fits.

An explicit `InputsChanged` result clears the current map route and stop metadata
until a matching route is ready. It also discards retained ETA and mileage even if
old stop IDs survive an import. New stop names must never be attached to stale
coordinates. The raw planning identity remains available for the existing fuel
actions; a later valid response republishes full geometry.

Inspecting a future stop leaves the selected truck's authoritative current dispatch,
route, polling and Next Loads identity unchanged. The panel reads the existing
dispatch-details endpoint and reuses at most 12 detail snapshots for five minutes,
keyed by truck/current-dispatch/future-dispatch. Cancellation and selection versions
reject replies after a switch or dismissal. The future panel matches server ETA
values to its exact selected stop and dispatch, using a fresh current chain or saved
detail ETA; the detail cache does not extend ETA validity. `NextLoadDetailsCard`
uses its embedded region variant inside the shared inspector without a second
close control or popup positioning. Standalone callers retain the original card
contract. Embedded future stops start compact. The page owns their Details state,
resetting it on a new inspection or stop, but not when held details or a forecast
arrive. The card reuses its existing arrival display memory in either density;
disclosure never publishes map state or requests more data. Back/Escape cancels
the detail request and clears only future-load
inspection; selecting a current stop or fuel station also dismisses it. The
current truck and route remain selected. Future distance includes
current remaining distance and all preceding saved legs/connections; an unknown
input stays unknown. These are display-only sums, without provider requests.
Current-stop cards refresh their appointment, ETA and remaining distance only when
the displayed values change, without reopening a dismissed card or coordinating
selection during polling. `routeLayer` samples displayed progress once per 60
seconds for both current-stop mileage and the shared header/future-distance
callback. Truck playback and road geometry continue independently. Ordinary
polling, fuel changes and camera movement do not reset this interval; route,
dispatch, truck or remaining-stop identity changes publish immediately. The first
valid sample after missing progress also publishes immediately. Route total
mileage is compiled once per geometry identity rather than summed on each frame.
Pending recalculation freezes the displayed pair, not the moving truck or road;
new ready data resumes mileage publication immediately. `FleetRouteDisplayMemory`
keeps raw planning/cache responses unchanged and supplies a presentation copy to
the current header, stop popups and future-stop inspection. A missing forecast
for the same route can reuse the previous one; explicit non-pending unavailable
results cannot. Retention is bounded by the original ETA validity plus 15 minutes,
without renewing that deadline on polling. Pending estimates keep the saved
values, text and status colors without Previous/Updating labels. JS label expiry
uses that same original deadline; quiet retention does not mean recalculation succeeded.
Truck, dispatch, next/passed stop, destination/appointment or completion changes
invalidate the presentation snapshot. Geometry-only plan/version changes retain
matching ETA and displayed mileage separately from the new geometry's progress
coordinates. The page sends ETA-only refresh metadata before awaiting its existing
HTTP request, so expiry during network latency does not erase the snapshot.
No new request loop, provider call or per-stop timer is introduced. This does not
extend server cache validity or request provider recalculations.
Current and future stops use fixed 30px circles with centered 13px white digits
and borders on opaque route colors. Current-load stops are blue; each future load
shares one palette color between its stops and loaded road. Empty connections
retain their separate amber role. Every stop occurrence, including repeat visits
within one load, keeps its own circle and exact selection identity. Stop inspectors
show the stable full-load position as `Load stop N of total`, separate from the
existing remaining/current or cross-load map marker numbering. Repeat visits to a
known complete address show `Visit N of total`; missing or incomplete addresses,
matching names and coincident coordinates alone never establish that label.
Current visit context includes passed reference stops, and future context requires
matching dispatch details and an exact stop ID. This metadata does not rebuild roads.
Coincident
circles use an evenly spaced screen-space layout without changing geographic
anchors. A small dot retains the road anchor when a circle is offset. Circle
backgrounds use small prepacked square icons, independent of text width. The whole
badge is selectable. Address, ETA and distance remain in click cards, not floating
map labels. Route strokes use a 5px minimum, the shared 1.75 width multiplier and 2.5px total
white outline, with station and stop markers still drawn above every road.
When the camera becomes idle, the base map uses hybrid satellite imagery at zoom
15 or greater and roadmap below 15. Unchanged map types are not reapplied. Click cards contain company, address,
compact local appointment, ETA and distance, omitting cargo and service notes.
The street and locality occupy separate lines on the left; appointment sits above
ETA in the right information column. ETA and Total use a shared value column.
Future cards also show the selected stop's Empty or Leg distance. The
load/order header starts the left column opposite appointment in both current and
future cards. Both put the dispatch-details link beneath a divider in the right column.
Current links use the authoritative dispatch ID carried by full and metadata-only
Client bridge payloads; a missing identity hides the link instead of using the route ID.
Content rows add no vertical gap beyond their normal line height. Shared, themed
dividers separate the stop type from the company, the address from a present
appointment reference, and ETA from distances, using only micro spacing. The
right-column details link retains its own divider and small spacing.
Production map details occupy the width-bounded shared inspector, preserving the same
logical reading order on narrow screens. Standalone renderer fixtures retain the
named card width. Only content exceeding the bounded inspector height scrolls;
there is no fixed 240px cap and no reserved blank area below it.
Stop cards use the existing planning response and metadata-only interop updates.
Current load/order identifiers come from the existing dispatch-detail response and
travel separately through `setLoadReference`, without sending route geometry or
requesting new data. Both sides match the authoritative current dispatch identity.
Details arriving before a route are replayed after it is accepted; late replies for
another selection cannot populate a current card. Each number can be copied
independently, without closing the selected stop.
Future geometry rendering does not format or cache removed floating-label text.
Appointment formatting preserves the supplied destination-local calendar and
clock without converting them to the browser timezone.

Current and future cards show `Appt #` below the address only when the selected
stop's notes contain an explicit standalone appointment-reference clause. The
bounded display parser honors pickup/shipper and delivery/receiver qualifiers,
rejects dates and BOL/load/order labels, and never renders the original notes.
This is display extraction from existing metadata, not address validation or a
database change. Unknown or ambiguous reference formats remain hidden.

## Verification

Run `bash test.sh fleet` for dependent feature and architecture checks. Unit tests
exercise session startup/disposal and station cancellation/failure races;
`FleetMapComponentTests` exercises the actual component's selection, preview and
Next Loads integration. None of these fake-interop checks proves live GPU behavior.
Follow [browser verification](../../Client/tests/browser/README.md) when changing
the JS provider or visible interaction.
