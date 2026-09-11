# Browser lifecycle probe

## Output retention

All probes now write by default to unique runs under repository-root
`artifacts/managed/browser-*`. Each probe prints its absolute output directory.
Automatic retention targets one GiB and seven days while protecting active runs,
the latest results and `.keep` pins. See [artifact retention](../../../docs/development/artifact-retention.md).
Explicit `*_OUTPUT_DIR` overrides remain caller-owned and bypass cleanup.

## Offline staged UI smoke

`AMFTMS_RELEASE_UI=1 bash verify-release.sh` from the repository root runs the
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

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/fuelEditorSmoke.mjs`
from Client exercises the actual staged fuel editor at 1440px/390px in both themes.
It checks full-tank gauges, adding and ordering visits, ten-gallon input, displayed
server errors, save/cancel behavior, stale dispatch callbacks and horizontal bounds.
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

`MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot node tests/browser/hoursForecastSmoke.mjs`
from Client checks the shared Road ETA/cycle display in the actual staged Dispatch
cards, the shared load popup and selected future pickup/delivery inspector. Ten scenarios cover 2344px/1920px/1440px/1200px/390px in
both themes: signed cycle balances, Cycle short without a green ETA, known
lateness alongside unknown cycle data, conditional recap alternatives, explicit
absence of reset alternatives even when supplied by the server fixture, and
the current-driver next recap with only its local date and credited hours. The selected
truck header checks fresh HOS clocks beside duty/rest text on wide screens, wrapping
without clipping on smaller screens, and fuel readings without maintenance wording.
Its selected truck and route panels form one width-bounded surface centered at the
map's top edge with the shared shadow and corners. The top inset is capped by
actual side clearance and disappears at full width; truck and route rows retain
their shared surface without a gap between them. First selection,
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

`node tests/browser/mapMarkersSmoke.mjs` renders the actual GPU truck, stop and
fuel-station layers on a synthetic geographic viewport at device pixel ratios 1 and
2. It checks the moving green heading arrow, gray engine-off circle and green
stationary circle (both explicit Idle and engine On at zero speed), with independent unit labels,
fixed round stop numbers, uniform 16px fuel circles without inside price text,
unchanged price colors and distinct `Fuel 1` badges. Mouse and touch clicks near
a circle's edge must select its station. Separate dense 161-point route probes
verify a continuous bright-blue current route and rounded future dashes with an
aligned white keyline; gaps expose the map and neither stroke adds route points.
Selecting a next load keeps its road bright and subdues the current and all other
roads; clearing selection restores their original appearance without widening them.
The cached high-precision dash extension computes offsets for existing vertices,
not extra route geometry. These checks do not measure production memory or
exercise Google Maps or live business data. Screenshots and
the report go to the managed `browser-map-markers` run; `MARKER_TEST_OUTPUT_DIR` overrides it.

`node tests/browser/mapStartupSmoke.mjs` checks startup camera visibility at desktop
and mobile widths with production host/truck code and a deterministic provider.
The measurable map host stays hidden through its first fleet fit, a truck deep link
skips the fleet-wide fit, reuse does not reset the camera, and empty/failed initial
data reveals safely without overriding a later user pan. All requests are intercepted;
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

For a release, `AMFTMS_RELEASE_BROWSER=1 MAP_TEST_URL=http://localhost:5067 bash
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

| Scenario | Growth (MiB) |
| --- | ---: |
| Plain provider, clear listeners | 9.99 |
| Plain provider, keep listeners | 9.96 |
| Provider with empty GPU scene | 10.08 |
| Provider with traffic layer | 10.54 |
| Full SPA map lifecycle, earlier run | 27.46 |

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
