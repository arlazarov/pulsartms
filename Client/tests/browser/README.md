# Browser lifecycle probe

## Output retention

All probes now write by default to unique runs under repository-root
`artifacts/managed/browser-*`. Each probe prints its absolute output directory.
Automatic retention targets one GiB and seven days while protecting active runs,
the latest results and `.keep` pins. See [artifact retention](../../../docs/development/artifact-retention.md).
Explicit `*_OUTPUT_DIR` overrides remain caller-owned and bypass cleanup.

## Offline staged UI smoke

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/dispatchCreationSmoke.mjs`
checks the compiled New load workflow at 1440/390/320px, light/dark themes and
200% root text. Synthetic address lookups and creation are intercepted in memory;
typing sends no requests, two selected addresses populate the stops, and one
explicit save opens the resulting workspace. It checks horizontal overflow and
captures screenshots. No live account, provider or database is involved.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/dispatchWorkspaceSmoke.mjs`
from Client exercises the actual compiled full-page Dispatch workspace. Use a
strict release publish; no running application server is required. The default
matrix covers 1440/390/320px in both themes and 200% root text at 390px. Set
`UI_TEST_BROWSER_CHANNEL=chrome` for installed Chrome or
`DISPATCH_WORKSPACE_CASE=1440-light-100` for one diagnostic case.

Synthetic identity, load, stops, forecasts, activity and document responses are
intercepted in memory. The probe verifies exact-stop navigation, native up/down
and drag reorder, deferred load Save, invalid-clock Discard, ETA invalidation for
draft changes, load-wide notes and retained issue resolution, immediate upload
after choosing files, and
the unsaved-navigation guard. Address/contact editors use native disclosures;
appointments and references remain visible. Unknown APIs and external requests
are blocked. It checks visible control bounds and captures screenshots plus a
JSON report in managed `browser-ui` output. This does not exercise production
data, real address verification, database transactions, document storage,
authentication, browser zoom or complete visual correctness.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/stationQuoteSizingSmoke.mjs`
checks single-day and comparison station quotes in a representative inspector
shell using the production popup module and staged CSS. Sixteen cases cover
320–1440px, both themes and 100%/200% text, including compact width restoration
when tomorrow's quote disappears. Back replaces Close when a truck is selected;
standalone quotes retain Close. Prices are synthetic; no APIs or writes occur.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/pageTransitionSmoke.mjs`
checks real Blazor navigation among Users, Settings, Fleet Map and Dispatch on
desktop and phone, in both themes, with normal and reduced motion. It pauses the
180ms opacity entrance at deterministic positions to verify unchanged geometry,
a retained layout/sidebar and only one page surface. Delayed settings responses
must not replay the effect. All APIs and the map provider are read-only synthetic
fixtures; no live authentication, database or external calls occur. Output uses
`browser-ui`; `PAGE_TRANSITION_TEST_OUTPUT_DIR` is an optional explicit override.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/mapToolbarSmoke.mjs`
from Client checks the actual Fleet Map toolbar at 1440/900/390/320px, in both
themes and with 100%/200% root text. It exercises native keyboard and pointer
toggles, date changes, search suggestions, active chip styles and the mobile
filter disclosure while checking that the map node and bounds remain unchanged.
It also verifies that IFTA precedes Fuel Stations and uses the same chip treatment
without an exposed square checkbox. Each case saves all four map preferences,
reloads the page and checks the restored controls and initial map options while
the date and search reset. A held storage read verifies that unconfirmed layer
states stay hidden without replacing controls or moving the toolbar.
All API responses and the provider module are
deterministic substitutes; no live requests or business writes occur.
Screenshots and the report use `browser-ui`.

`HOURS_TEST_LIFECYCLE=1 MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/hoursForecastSmoke.mjs`
also exercises sixteen SPA transitions between Dispatch and the selected-truck
map, using the existing synthetic read-only fixtures. This mode runs one desktop
light-theme scenario, not the default responsive matrix. It verifies that disposed
maps stop route polling, captures post-GC JavaScript heap/DOM/listener counts and
checks settled document/listener bounds. No heap files, live authentication,
provider calls or database operations are involved. JavaScript heap readings do
not establish .NET WASM heap, GPU or production memory retention.

The default hours forecast matrix also checks bounded phone truck scrolling.
On phones, the compact truck row retains title, remaining distance, Details and
Close. Details reveals telemetry, HOS, GPS location and route facts in the bounded
inspector; Hide returns to the compact row. Portrait, short-screen and 200% text
checks retain keyboard access to the truck actions and unchanged map bounds
without clipping overflow.
Readings also sweep 320–767px at 100% and 200% text: telemetry stays compact,
HOS sits beside it when space permits, and whole groups wrap without shrinking
icons or clocks. Screenshots cover the stacked phone and adjacent wider layout.
The Fleet-only matrix also checks explicit Show route interop without selection
auto-fit, repeated background clicks retaining the truck and both panels,
Back replacing Close, and equal rendered text gaps for appointments and ETA.
Map-provider behavior is covered separately by the map JavaScript tests.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/brandSmoke.mjs`
from Client checks the actual staged anonymous Login and authenticated Users/sidebar
at 1440/768/390/320px, in both themes and at 100%/200% root font size. Set
`UI_TEST_BROWSER_CHANNEL=chrome` to use installed Chrome; otherwise it uses
Playwright Chromium. Use an artifact from a strict release publish. The probe
requires no local server, account, credentials or database: its identity and four
read-only API responses are synthetic, and every other API, write and external
request is blocked. It does not sign in or contact a map provider.
Anonymous Login normally resets to Light. Its dark artwork cases explicitly
apply the dark theme after mount; authenticated cases use the account fixture.

The 32 application cases verify accessible shared SVG references, logo/container
bounds, preserved sidebar account identity and reachable sign-in controls without
document-level horizontal overflow. Two standalone brand-guide cases cover desktop
and mobile in fixed light mode. A separate sizing fixture displays the staged
favicon at actual 16px and 32px sizes. Screenshot pixel counts check that the
wordmark body, TMS descriptor, red/coral pulse and small icons actually paint;
tiny-icon checks include antialiased colors against the supplied white backing.
An SVG bounding box alone cannot establish that an external symbol rendered.
The managed
`browser-ui` run retains a JSON report, full-page screenshots and logo/icon
crops for inspection. These checks do not establish exact reference-image fidelity,
complete visual correctness, browser zoom, real authentication or live API behavior.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/routeEditorSmoke.mjs`
from Client exercises the production Blazor load route editor at 1440/768/390/320px
in both themes. It selects alternatives through the map callback contract, adds
an address and a simulated dragged via point, checks responsive bounds and the
unchanged map element, and confirms an intercepted save. APIs and the Google
map are explicit deterministic substitutes; no real writes, provider requests,
GPU interaction or native Google drag behavior are tested. Screenshots and the
report use the managed `browser-route-editor` artifact kind.

`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` from the repository root runs the
offline release gate and then `test:ui`. Install Playwright Chromium first with
`npx playwright install chromium` from Client, or set `UI_TEST_BROWSER_CHANNEL=chrome`
to use installed Chrome. GitHub Actions installs Chromium and enables this check.
For an already-verified artifact, set `MAP_TEST_ARTIFACT_DIR` to its absolute
`publish/wwwroot` and run `npm run test:ui` from Client.

This boots the real staged Blazor WASM for Dispatch, Users, Settings, Fleet Map and
Add User. It checks 1440px/390px layouts in both themes at 100%/200% root font size,
plus Dispatch-only cases at 2344px. Checks cover heading alignment, control bounds,
readable Dispatch text, adjacent next-stop appointments, responsive stop/summary
columns, legacy per-stop cycle snapshots, current-driver recap distinct from post-delivery
recap, removal of the cumulative driving/rest block as a delay explanation, consistent
configured load prefixes and independent blank/custom-prefix settings previews,
job-specific pickup/delivery references without actual-date rows, and basic non-saving
interactions. Dispatch checks also switch Cards/Table/Papers and Active/Completed,
retain completed pickups, display server-provided rate and per-mile values, and
ensure completed views do not start live planning or equipment polling. APIs are
deterministic read-only fixtures, the map provider module is explicitly stubbed,
and all network/writes are blocked. No local server, provider credentials or login
is needed. Results, 44 page screenshots and initial/scrolled Dispatch dialog screenshots
are saved to the managed `browser-ui` run.
This does not validate the GPU map, live authentication, browser zoom or all layouts.
Each Dispatch scenario also imports a five-stop fixture with three separate visits
to the same Webster address. Cards and Details retain every identity in route
order, each visit's 12-hour appointment, the completed first pickup, visit context
and pickup/delivery counts. Additional screenshots cover the expanded timeline
and the scrolled load dialog at both font scales. Manual completion probes open an
individual repeated visit, validate an invalid actual time locally, check control
bounds, capture the form and cancel without sending a write. Component and server
tests cover confirmed writes, undo, authorization, stale revisions and sync retention.
More details opens a full-width stop view inside the existing dialog. Keyboard
checks verify that All stops restores the overview scroll position and opener
focus. A separate single-load fixture verifies the same card width as a multi-load
row and a compact No next load placeholder beside it (below on phones), absent
during initial loading, in the archive and when a next load exists. It retains
four completed visits as short numbered rows beside remaining work on wide cards
and above it on narrow cards.
Screenshots include this completed-history layout at both font scales.
Table probes keep multi-stop rows bounded to one visible visit per group, with
total/completed counts and the next unfinished visit's original position. Keyboard
activation of the count opens all five visits in the shared dialog without growing
the table row. Separate checks retain later delivery appointment windows there.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/fuelEditorSmoke.mjs`
from Client exercises the actual staged fuel editor at 1440px/390px in both themes.
It checks full-tank gauges, adding and ordering visits, five-gallon manual input
from a 25-gallon minimum, and server-prepared 25→35 / 100→90 redistribution with no
quantity-preview HTTP request. It also checks displayed
server errors, save/cancel behavior, stale dispatch callbacks and horizontal bounds.
Calculate automatically submits one reset immediately without a confirmation;
opening the editor, Cancel and Escape do not write.
Real mouse and touch pointer drags cross fixed pickup/delivery anchors and fuel
rows; keyboard arrows exercise the same ordering contract. It also checks
server-provided purchase costs, selected-station focus callbacks, visit-number
contrast and mobile Route/Fuel/Map panes inside the existing map bounds. Selection
and quantity controls are checked after scrolling the mobile editor.
Selected-truck HOS probes cover 320–2000px widths, both themes and 100%/200%
root font sizes, including two-digit hours and a custom wrapper dial size.
All API calls, including simulated saves, are intercepted fixtures; no business
records or provider requests are made. Screenshots and the JSON report go to
the managed `browser-fuel-editor` run; `FUEL_EDITOR_OUTPUT_DIR` overrides that path.
This verifies UI interaction and layout, not live fuel calculations or GPU selection.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/stopDetailsSmoke.mjs`
from Client runs a separate future-stop details scenario against the actual staged
Blazor UI. It checks pickup/delivery appointments, local ETAs, miles/kilometres,
the shared top inspector, retained hidden truck/route content, Back, Close
and Escape at 1440px/390px in both themes. The provider module is explicitly stubbed
with deterministic selection callbacks; all API responses are fixed and no requests
reach a server or map provider. Initial and scrolled screenshots plus the JSON report
are saved to the managed `browser-stop-details` run; `STOP_DETAILS_OUTPUT_DIR` overrides
that path. This covers Razor layout and selection interaction, not Google Maps,
geographic rendering, live authentication or real route calculations.
The next-stop checks also start with a compact card, expand and collapse it using
Enter, Space and pointer input, and verify stable controls, the retained map and
card nodes, hidden supplemental fields and no disclosure-triggered API requests.
Selecting another stop starts compact again.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/nativeInspectorSmoke.mjs`
checks the real docked inspector controller with the production current-stop and
station layers and HTML renderers. Its eight cases cover 1440px/390px, both themes
and 100%/200% root font size. Assertions cover stop ETA/address, station quote,
server-supplied purchase cost and gauges, switching ownership, focused-content
retention during polling, Back/Close/Escape focus, one persistent native host,
absence of lower cards, unchanged map bounds and scroll access without horizontal
clipping. Map and marker ports are deterministic substitutes; the fixture does
not render Blazor or Google Maps and does not calculate fuel economics. All
network calls are blocked. `INSPECTOR_OUTPUT_DIR` overrides the default managed
`browser-native-inspector` report and screenshot directory.
The same eight cases also select all five stops of a repeated-site route. They
check distinct visit appointments, full-load positions, repeated-visit labels,
reachable inspector content and the retained map bounds.

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/hoursForecastSmoke.mjs`
from Client checks the shared Road ETA/cycle display in the actual staged Dispatch
cards, the shared load popup and selected future pickup/delivery inspector. Ten scenarios cover 2344px/1920px/1440px/1200px/390px in
both themes: signed cycle balances, Cycle short without a green ETA, known
lateness alongside unknown cycle data, conditional recap alternatives, explicit
absence of reset alternatives even when supplied by the server fixture, and
the Dispatch current-driver next recap with only its local date and credited hours. The selected
truck header checks fresh HOS clocks above current duty text, wrapping
without clipping on smaller screens, and fuel readings without maintenance wording.
Its selected truck and route panels form one width-bounded surface centered at the
map's top edge with the shared shadow and corners. The top inset is capped by
actual side clearance and disappears at full width; truck and route rows retain
their shared surface without a gap between them. The default desktop compact view keeps
speed, fuel and engine readings aligned and visible, fuel controls reachable,
trailer beside the driver, enlarged equal-sized telemetry icons, and current duty
directly below all four unchanged HOS clocks without Next recap. The GPS block retains
its timestamp and Route & load details link. Outside temperature uses one compact Fahrenheit/Celsius row directly
below the three telemetry readings, with no new action or column. The load link retains
its disabled placeholder while the load identity is pending. Desktop Details reveals only
the lower load section: adjacent load/order metadata, three-line remaining distance,
destination and inline appointment/ETA columns on wide cards, with metadata above
the other groups at intermediate widths. The upper content and clock dimensions
stay fixed through desktop disclosure. On phones, selection starts with a narrow
truck title strip, centered remaining distance in the configured primary unit, and
accessible Details/Close controls. The distance stays visible with a stable slot
through pending data, Details and Hide; it adds no desktop duplicate. Details opens
the retained readings, HOS, GPS location and route/load information; Hide restores
the strip without clearing the selection or moving the map. Title, remaining
distance, Details/Hide and Close retain the same bounds in every state. The one
disclosure supports keyboard activation and retains the map bounds. Its collapsed
state and stable title controls are checked in both themes; desktop keeps all
content visible.
Neutral address and forecast placeholders reserve
the ordinary loaded geometry while the lower section is open during a pending read.
The saved preview intentionally omits appointment dates: Delivery retains the same
timing-row element and position while the load reference supplies its date and the
live plan catches up. It never renders under Load. The single appointment above ETA
belongs to the tracked next stop, not a later delivery.
Intermediate-stop layout probes place the distance in its pickup/delivery heading,
not in another vertical metric below Remaining. Cloned inspector screenshots isolate
that layout from live route calculations, checking inline alignment on wide cards
and overflow at narrow widths in both unit configurations.
Narrow cards stack the readings, HOS and action groups;
expanding retains the shared width cap without changing the map bounds. Both densities retain the selected truck's
provider-supplied GPS address and exact observation timestamp independently of
the next route stop. First selection,
pending/complete reads, repeated selection and clearing must retain the same map
element, native inspector host and exact viewport rectangle at desktop and mobile
widths. Future-stop details replace the visible truck content in that same top
inspector; the retained truck components remain hidden and no lower popup opens.
Dispatch's wide header groups identity, status/fuel and mileage, and HOS together
on the left without elastic gaps, with duty/rest and Next recap in the shared strip
beneath. Fleet HOS circles keep equal compact gaps even on spacious screens.
Real component polling receives empty pending
forecasts and must retain the previous ETA, cycle, recap and alternative values,
including the same status text and colors without Previous/Updating labels.
Held polling responses also cross a short fixture validity deadline using the
browser's controlled clock. Dispatch and Fleet ETA cards must stay unchanged while
HTTP is pending, then replace each complete forecast without an empty intermediate render.
The same scenarios verify the absence of a standalone future-trip fuel summary
and retention of server fuel quantities, truck-relative distance and dated
historical schedule metadata on the map bridge through ETA refresh.
Checks also cover English text, street-first two-line current addresses with full-value
copying, and horizontal bounds. Initial, pending and refreshed screenshots and the
JSON report are saved to the managed `browser-hours-forecast` run;
`HOURS_TEST_OUTPUT_DIR` overrides that path. The script intercepts every request and
uses deterministic map callbacks and API fixtures. It does not exercise GPU
rendering, real provider requests, authentication, database state, server calculation
accuracy or production performance.

The 2026-09-08 local run passed all 44 cases against a fresh strict publish, with
zero browser errors, unexpected requests or checked geometry failures. It caught
the Users heading's former vertical offset before the shared PageHeader alignment
fix; the complete matrix passed after rebuilding and republishing. Screenshots and
the JSON report are local under `test-results/ui-smoke`, not committed baselines.

## Offline GPU trucks and stop cards

`node tests/browser/mapRemountSmoke.mjs` uses the production retained map host,
scene and installed GoogleMapsOverlay/Deck adapter. A synthetic stationary map
holds its first camera frame after GPU initialization, then honors the public
redraw request. Four mounts at each of DPR 1 and 2 verify hidden startup layers,
correct camera projection without panning, and zero remaining overlays, canvases
or listeners after disposal. The fixture reads adapter internals for assertions
only; production does not. Screenshots and the report use `browser-map-markers`.
This isolates adapter startup/remount ordering, not live Google Maps or API data.

`node tests/browser/fuelMarkerVisibilitySmoke.mjs` checks the production station
controller and GPU points using the offline stop-card fixture. Four light/dark,
DPR1/2 cases hide ordinary stations, retain the planned price color and visit
badge, open its popup with a real pointer click and restore the full layer. No
provider or API requests are made. Evidence uses the managed `browser-stop-cards`
directory; the broader stop-card suite remains a separate check.

`node tests/browser/mapMarkersSmoke.mjs` renders the actual GPU truck, stop and
fuel-station layers on a synthetic geographic viewport at device pixel ratios 1 and 2. It checks the moving green heading arrow, gray engine-off circle and green
stationary circle (both explicit Idle and engine On at zero speed), with independent unit labels,
fixed round stop numbers, uniform 16px fuel circles without inside price text,
unchanged price colors and distinct `Fuel 1` badges. Mouse and touch clicks near
a circle's edge must select its station. Separate dense 161-point route probes
verify a continuous bright-blue current route and rounded future dashes with an
aligned white keyline; gaps expose the map and neither stroke adds route points.
Selecting a next load keeps its road bright and subdues the current and all other
roads; clearing selection restores their thinner dashed appearance. Overview groups
exclude the selected truck and expand through real mouse and touch clicks;
close zoom restores individual identities. Group labels sit directly at their
geographic center. Pixel checks verify the label at both densities, and clicks
zoom to that same geographic point without any label displacement.
Group expansion preserves the selected truck and both displayed roads without
selecting a group member. Component/interop tests separately retain the inspector
and open Details while Follow stops, without data requests or deselection.
Close-zoom probes place 11005 and 54777 a few pixels apart, retaining both GPS
positions while separating their labels. Mouse and touch activate each label
independently; screenshots show the short connector at both pixel densities.
Saved-route probes also exercise the production route layer with unknown, known
and subsequently missing progress. GPU pixel checks verify that the cold saved
road paints, valid progress trims it, and a later gap retains that trimmed road
without publishing fabricated mileage.
Three consecutive edit/cancel cycles must restore the same trimmed road without
re-publishing geometry; GPU pixel checks verify the road actually paints again.
The cached high-precision dash extension computes offsets for existing vertices,
not extra route geometry. These checks do not measure production memory or
exercise Google Maps or live business data. Screenshots and
the report go to the managed `browser-map-markers` run; `MARKER_TEST_OUTPUT_DIR` overrides it.

`node tests/browser/mapStartupSmoke.mjs` checks startup camera visibility at desktop
and mobile widths with production host/truck code and a deterministic provider.
The measurable map host stays hidden through its first fleet fit, a truck deep link
skips the fleet-wide fit, reuse does not reset the camera, and empty/failed initial
data reveals safely without overriding a later user pan. Real ResizeObservers exercise
the first inspector appearance, Details/Hide and delayed content before any map gesture:
none may mutate the camera, while subsequent explicit focus uses the updated insets.
With Follow enabled, subsequent synthetic truck movement must keep the captured
screen anchor through disclosure and delayed content on both screen widths.
Explicit reactivation and actual map resizing must capture the new free region;
further disclosures cannot change that updated anchor.
All requests are intercepted;
this does not verify Google Maps animation behavior. Results go to
the managed `browser-map-startup` run; `MAP_STARTUP_OUTPUT_DIR` overrides the directory.

After `npm run styles:build`, run `node tests/browser/stopCardsSmoke.mjs` from
Client. This separate fixture renders the production GPU scene, moving/idle/off
trucks at representative headings, current and selected future numbered stops
without floating labels, and the production fuel popup for a
style reference. Real canvas clicks open the current stop's production details
card; its appointment window, ETA status and distance are checked at desktop and
mobile widths. It uses installed
Chrome by default; `UI_TEST_BROWSER_CHANNEL` selects
another installed Playwright channel. `STOP_CARD_OUTPUT_DIR` overrides the managed
`browser-stop-cards` output directory.

The fixture checks light/dark map-like backgrounds at device pixel ratios 1 and 2,
28px green moving/idle and gray engine-off glyphs, unchanged 13px truck numbers, high-density truck font atlases,
and selection colors. Current and future stops use fixed 34px circular images with
15px white digits, an opaque route-colored surface and a white border; digits have
no text-sized background. Additional screenshots isolate actual GPU stop layers from
the roads and decorative fixture grid and measure equal width/height for pickup and delivery
numbers 2/3 and 10/11 at both pixel densities. A second digit must not resize the
circle. Offset markers can also retain a small selectable geographic dot.
Selected future circles and roads retain
their emphasis across polling and toggle cycles without generating floating labels.
The current road uses 5px for its normal zoom weights; next-load routes stay 5px
when selected, with rounded dashes and a thin aligned white keyline. Selection
keeps that load bright and subdues every other road, including current, without hiding them.
Recommended fuel stations keep their price colors and GPU rings, with compact
dark rounded `Fuel 1` order badges above them; repeat visits share `Fuel 1/2`.
These badges remain distinct from pickup/delivery circles and do not include distance.
Clicking the badge or station opens its production popup and truck progress updates the distance
there without changing route emphasis. Its street/locality address stays on two lines
while copying the complete original value. Four additional fuel popup screenshots cover
both themes and device densities. They verify server-selected diesel prices, numbered
repeat visits and paired on-arrival/after-fueling gauges using physical tank capacity.
Two additional 390px fuel-card cases check both themes and ordinary two-visit height
without internal scrolling. Four single-visit cases at 1320px/390px check both themes,
full-row cards, centered compact gauges and absence of clipping. Canvas clicks on
the station circle open its popup; visit numbers also remain inside the cards, and compact
visible distance retains the full accessible description. Marker price colors remain
unchanged; these checks do not calculate purchase economics
in the browser.
The current popup checks the two-line address, job-specific appointment references,
compact appointment above inline ETA and Total,
separated labels and values, bottom-center placement, horizontal overflow,
scroll access to distance, Escape and close-button dismissal. Inspect the four GPU
screenshots and sixteen initial/scrolled pickup/delivery popup screenshots as well as the JSON
report. Twenty additional Road ETA/cycle cases cover both themes at desktop/mobile
widths, including verified shortfall, unknown cycle, conditional alternatives and
pending retention on the current stop's production popup. These check English text,
horizontal clipping and ordinary card height without internal scrolling. All browser
requests are intercepted and unexpected requests are blocked;
no local server, Google Maps, credentials or database is used. The synthetic
orthographic layout does not validate provider integration, geographic projections,
crowded stop placement or the Blazor selection panel.

## Authenticated map lifecycle

`node tests/browser/truckPlaybackSmoke.mjs` from Client opens a separate Chrome
window with synthetic GPS and the production truck playback layer. It switches
to another tab, advances the fixture clock by ten minutes, and tests snapshots
both before and after visibility restoration. It also minimizes the browser
while the GPS buffer is exhausted and resumes with new telemetry. It checks no hidden paints,
normal-time playback after rebasing, and retained Follow camera alignment.
The isolated browser connects without Playwright's default focus emulation so
Chrome really marks background/minimized documents hidden. The provider is a
deterministic substitute; no API calls or business writes occur.
Results use a managed `browser-map-startup` directory. This checks real Chrome
visibility, not Google Maps rendering or live provider delivery latency.

`MAP_TEST_SOAK=1 MAP_TEST_HEAP=1 npm run test:browser` runs 30 full page cycles.
Each selects truck 11006, follows for five seconds, zooms out manually, resumes
Follow for five seconds, and leaves for Dispatch. Active and post-disposal heap
samples are recorded after GC; progress is saved after every cycle. This is a
bounded interactive soak, not an overnight test or a GPU-memory measurement.

Run `npm run test:browser` from Client with the local client and API already running.
Google Chrome must be installed. A separate visible browser opens; sign in there
if prompted. No credentials or storage state are saved by the test.

The probe makes 12 SPA round trips between Fleet Map and Dispatch, checks map
creation/removal and browser errors, and records JS heap after explicit garbage
collection. The first three cycles are warm-up. Results are written under
the managed `browser-map-lifecycle` run. `MAP_LIFECYCLE_OUTPUT_DIR` overrides it.
No business records are created or edited.

Heap change is diagnostic, not a leak verdict or GPU-memory measurement. Compare
multiple runs with the same fleet data and inspect a sustained upward trend before
setting a project-specific CI threshold. Authentication and provider access are
required; this probe is separate from the offline unit suite.

For a release, `PULSARTMS_RELEASE_BROWSER=1 MAP_TEST_URL=http://localhost:5067 bash
verify-release.sh` from the repository root runs the offline gate first, then this
probe against its exact staged Client artifact. `MAP_TEST_ARTIFACT_DIR` is supplied
by the gate: Playwright serves that directory's static files at the local origin
while forwarding `/api` to the already-running local backend. No alternate server,
credential export or production deployment is needed. External map requests still
use the configured provider, so this mode is explicitly opt-in.

Set `MAP_TEST_HEAP=1` to capture heap snapshots after cycles 3 and 11, then again
after ten seconds and garbage collection. Snapshots can contain sensitive runtime
data: keep them local in the ignored managed run, never commit or upload them.
Pin evidence with `.keep` if it must survive the normal retention policy.

Set `MAP_TEST_PROVIDER_ONLY=1` for a provider control run. After one normal map
startup and navigation to Dispatch, the probe creates and removes 12 plain Google
maps in temporary containers, without fleet layers. It uses the already loaded SDK
and does not change authentication, import maps or application files. This control
isolates provider recreation, not all page lifecycle costs; compare trends rather
than attributing the entire difference to fleet code.
Add `MAP_TEST_KEEP_PROVIDER_LISTENERS=1` to omit broad provider listener cleanup,
`MAP_TEST_PROVIDER_SCENE=1` to include an empty fleet GPU scene, or
`MAP_TEST_PROVIDER_TRAFFIC=1` to include Google's traffic layer. Each combination
writes a distinct result name. Canvas counts are checked so a missing render layer
cannot produce a misleading memory improvement.
`MAP_TEST_PROVIDER_REUSE=1` retains one provider map in the test document while
recreating optional overlays. `MAP_TEST_SELECTION=1` checks selecting truck 11006
and toggling Follow after the full SPA cycles while delaying planning responses by
six seconds; it requires that truck in local data.

## Local control results (2026-09-07)

Retained JS heap growth between mean cycles 3–5 and 9–11, after explicit GC:

| Scenario                            | Growth (MiB) |
| ----------------------------------- | -----------: |
| Plain provider, clear listeners     |         9.99 |
| Plain provider, keep listeners      |         9.96 |
| Provider with empty GPU scene       |        10.08 |
| Provider with traffic layer         |        10.54 |
| Full SPA map lifecycle, earlier run |        27.46 |

The provider controls reproduce sustained retention without fleet data. Empty GPU
and traffic overlays do not explain the full SPA difference in these runs. Full
SPA cycles include additional page work and different timing; the difference is
not a measured fleet-layer leak size. These results neither prove a provider bug
nor establish long-run cache bounds. No production fix follows from these numbers
alone. All application files were unchanged during the control runs.

After introducing the bounded provider host, a full 12-cycle SPA run retained
1.08 MiB between the same sample windows. Native WebGL2 context count was one
at both cycles 3 and 11, versus four and twelve in the pre-change snapshots.
Both canvases rendered on every mount. One provider map is intentionally retained;
this is not a claim of zero allocation or a direct GPU-memory measurement.
The initial selection smoke run exposed a late route fit cancelling Follow.
Route fitting now consults the live Follow state. The regression run passed with
a six-second planning-response delay: Follow remained active after route arrival
and could subsequently be toggled off. Both canvases rendered through all 12
preceding mounts; retained heap growth was 1.11 MiB in that run.

### Active soak after the fixes

A 30-cycle run completed in 408 seconds with truck selection, Follow, manual zoom
and Follow resumption on every cycle. All checks passed; both canvases rendered
on every mount. At cycles 3 and 29 the snapshots contained one native WebGL2
context and three HTMLCanvasElement objects. Counts of the named dispose, notify
and refresh closures were unchanged (28, 4 and 5 respectively).

Post-disposal JS heap ended at 28.48 MiB, versus 32.05 MiB while active. Growth
between the usual averaged sample windows was 3.89 MiB; total first-to-last growth
was 7.44 MiB. Across the final ten cycles, endpoint growth was 0.61 MiB and the
first/last three-sample mean difference was 0.36 MiB. Retention slowed but did not
reach a proven zero-growth plateau. This run supports that the context accumulation
is fixed; it does not rule out every leak or prove all remaining growth is caching.

## Account appearance

Run `node Client/tests/browser/appearanceSmoke.mjs` from the repository root
with `MAP_TEST_ARTIFACT_DIR` pointing to a staged Client `wwwroot`. It checks
Light/Dark and unit selection, account-menu navigation, account isolation,
restoration in a new browser context and desktop/mobile controls using synthetic
authenticated APIs. It never writes
to the application database or calls providers. Other staged UI fixtures also
serve the chosen theme through the account appearance endpoint.
