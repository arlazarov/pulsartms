# Shared UI controls

Follow the [PulsR TMS brand design](design/brand-design.md) for all new and changed
branded UI. Use the selected Pulse SVG through the shared `BrandLogo`
component, not font-recreated lettering. **PulsR TMS** is the full display name;
**PulsR** is its short form, with **TMS** kept in the supplied logo lockup. Carrier
identities and load prefixes remain business data; see
[product naming and compatibility](development/product-branding.md). Branding
does not change operational colors, control sizes or the existing theme contract.

## Component ownership

`Components/` owns generic controls such as tables, forms and popups. `Shared/`
owns reusable application UI, grouped by responsibility: `Brand`, `DriverStatus`,
`Drivers`, `Fuel`, `Dispatch`, `Search` and `Trucks`. A Razor/code-behind pair lives in a component
folder inside its group, for example
`Shared/DriverStatus/DriverHours/DriverHours.razor` and `DriverHours.razor.cs`.
Standalone helpers stay in their owning group; they do not need one-file folders.
Code-behind namespaces follow their folders, and Razor imports expose the
component namespaces.

`Shared/Brand/BrandLogo/` owns product identity rendering and consumes the external
`wwwroot/brand/pulsr.svg` symbols. Layouts and pages place this component; they must
not duplicate or redraw the wordmark. The favicon uses the matching five-bar
pulse symbol, not a letter from the wordmark.

`Pages/<feature>/` owns route components, page coordination and components used
only within that feature. A component used by multiple features belongs in the
appropriate shared group, together with its reusable presentation helpers.
Shared UI, models and services must not import page namespaces. DTOs live under
`Models/DTO/` in their feature folders and must not depend on UI components.
For example, both Fleet Map and Dispatch use `Shared/DriverStatus/DriverHours`,
while its input model is `Models/DTO/Planning/DriverHosClocks`.

Shared compatibility load dialogs and their stop/cycle children live together
under `Shared/Dispatch/`. The full-page load workspace and its editor components
belong to `Pages/Dispatch/`. Page-local load cards, tables, paper views and
map-selection components remain with their pages. Moving reusable UI must not
move polling, selection or request ownership out of the page that controls it.

## Tokens and themes

Primitive palette values live in `base/_colors.scss`; semantic theme maps live in
`base/_themes.scss`. Import `@use '../base' as ui` from UI styles. Use `ui.theme(surface)`,
`ui.theme(text)`, `ui.theme(border)` and other semantic roles for interface colors;
use `ui.clr()` for intentional fixed palette colors such as chart series.
Do not add hex/RGB literals in page, layout or component styles.
`data-theme="dark"` and `data-theme="light"` override semantic CSS properties.
The dark palette is an initial theme foundation, not a completed visual audit of
maps, document previews or provider-rendered content.

The account disclosure places Personal settings beside Logout for every
authenticated user. `/settings/personal` contains Appearance and Units;
the main Settings navigation and `/settings` contain Admin-only fleet forms.
Light/Dark, Fahrenheit/Celsius and miles/kilometers/Both are account
preferences. Save each explicit selection automatically on the server; a fresh
sign-in or page reload restores it on any device. Company settings must not
override personal units. Load numbering remains a shared fleet preference.
Do not store a shared browser theme or add polling/push for this preference.
Apply initial theme and units before mounting authenticated pages.
During the initial read, show a compact blue
three-dot pulse instead of a visible preferences-loading sentence. Keep a
screen-reader loading status and disable motion for reduced-motion preferences.
Loading failures
leave the app usable in Light and expose a retry in Appearance. Failed saves
retain the confirmed theme. Account changes cancel reads/writes and discard late
responses; logout returns to Light. A theme change only updates semantic roles,
without changing layout, operational colors or business calculations.

The Google basemap reads the account theme at mount. Its scheme is immutable,
so returning from Settings with a different theme replaces the retained provider
map while carrying its last center/zoom. Retain only one map, and reuse it on
ordinary navigation with the same theme. Satellite imagery is not recolored.

Use named `ui.pg(sm)`, `ui.pg(md)`, `ui.pg(section)` spacing and
`ui.fs(body)`, `ui.fs(small)`, `ui.fs(title)` typography tokens. Their values are
defined in `_variables.scss` and exported as rem-based CSS custom properties.
Numeric font-scale calls remain compatible; numeric spacing calls are rejected. Names are validated at
compile time. New reusable sizes belong in the token maps, not repeated literals.
Pixel values remain appropriate for hairline borders, exact map geometry and
viewport breakpoints; do not replace them with unrelated spacing tokens.

A width at which a layout changes shape is a name in `$breakpoints`, read the
same way whether what is measured is the window or an element's own box.
`ui.breakpoint(name)` answers that one width; `@include ui.below(name)` and
`@include ui.above(name)` write the media query, and a container query names
the width the same way: `@container <name> (width < #{ui.breakpoint(name)})`.
Do not hand-write `@media (max-width: …)` or put a bare rem or pixel width in
a container query; checks reject both. Prefer the element's own width over the
window's wherever what changes is how that element is laid out.

What stands in front of what is a name in `$layers`, read with `ui.layer(name)`:
from `raised` and `sticky` inside a card, through the three things that float
over the map, to `dropdown`, `dialog` and `viewer`. Bare `z-index` numbers are
rejected; a new rung belongs in the map, where the order can be read.

A component owns how it is read. Where a place needs the same facts said
differently - the arrival forecast as one inline line, the same forecast
quieter inside a map card, a table whose rows become cards when there is no
room for columns - the variant belongs in the component's own stylesheet, and
the place asks for it by name (`Reading="inline"`, `Reading="cards"`). Anything
finer is a custom property the component publishes, such as
`--stop-hours-road-size` or `--hos-clock-min-width`. A page must not name
another component's `__element` classes; checks pin this for the HOS clocks,
and the same rule applies to every shared component.

## Shared visual hierarchy

Use white `surface` cards on the blue-gray `canvas`, with `surface-soft` for quiet
table headers and grouped controls. Separate sections with `border-subtle` and
named spacing rather than nested shadows. Normal cards and controls use
`radius(sm)`; floating dialogs may use `radius(md)` and the shared popup/modal
shadow. Headings use navy `text`, supporting information uses `text-secondary` or
`text-muted`, and the primary action uses the existing blue action roles.

Settings integration cards expose only TorqueAI, Samsara and Google for Emails.
Each provider has its own Edit, Save and Cancel flow, separate from load numbering
and fuel preferences. Configured reports credential presence, not a verified
connection. Replacement password inputs start empty; blank fields preserve saved
values, and entered values are cleared after saving, cancelling or leaving the
component. Never read back or persist secrets in browser storage. Using server
configuration requires an explicit confirmation for that provider.

Use `selected` with `selection-border` for an active editing state, and filled
`action` with `on-accent` for selected view/scope buttons. These interface roles must not replace fuel price, pickup/delivery, route
or HOS status colors. Table actions stay low-emphasis until hover or keyboard
focus; destructive actions retain their danger role. Keep the navigation compact
and preserve visible keyboard focus, responsive wrapping and minimum touch sizes.

Use `size(content-account)` for account forms and `size(content-login)` for the
sign-in card instead of fixed pixel widths. The fuel editor is a compact floating
card over the existing Fleet map, not a second map workspace. Keep the map mounted,
full-sized and independently interactive, and leave background controls accessible.
Trucks are always shown; there is no manual truck-layer visibility toggle.
While editing fuel, show only the edited truck; restore all trucks when editing
ends, including empty drafts.
On wide screens, center the editor horizontally while retaining its bottom map inset,
and give the route timeline a full-height left column beside the
selected station, quantity and prices. The timeline and details scroll separately,
while the header and save footer remain visible. Narrow layouts stack the two
scroll regions and retain an uncovered part of the map on tablets. On phones, the
editor fills the existing map bounds without a top gap. Route and Fuel details
switch between full-height working panes; header and save footer remain visible.
Map collapses the editor to its header and view controls without discarding the
draft, exposing the same interactive map. Selecting a station opens Fuel details.
Do not cap a wide editor's
timeline to a short strip that hides its pickup/delivery anchors. Editor sizing
must not increase spacing across unrelated pages.

Map information uses one contextual inspector, centered horizontally with an `md`
gap below the map's top edge, underneath the page header and filters, capped by
the smaller actual side clearance. As the centered inspector approaches the map
edges, top and side gaps shrink together; a full-width inspector has no top gap.
Use the existing overlay geometry observer, not device breakpoints. Use a small
shared corner radius and subtle card shadow, without a modal backdrop. Bound its width with
the named inspector size token instead of stretching across spacious maps; use
the available map width on narrower screens. Keep data groups compact and
left-aligned inside it. Truck, current/future stop and fuel selections
replace its content rather than adding separate information cards. The truck and
route rows share one surface and divider, without gaps between those rows.
Back restores truck information without clearing its route. Closing the truck
card deselects the truck and clears its displayed route and follow state; closing
a stop or fuel inspector hides only that inspector. A blank-map click retains
the truck card, its visible panels, selection and road, even on repeated clicks.
It returns a stop/fuel card to the selected truck. Without a
selected truck it closes the card. Marker and route clicks are not background clicks;
active editors and camera dialogs ignore background dismissal.
Back to truck replaces Close on stop/fuel inspectors; never show both controls.
Truck cards and standalone station cards retain Close.
The truck inspector uses the `map-compact-inspector` width cap (56rem), with
adjacent telemetry and HOS above the load-information panel. Both panels remain
visible on larger screens. On phones, the inspector opens as one compact header
row with truck title, remaining distance, Details and Close. Details reveals the
driver, trailer, actions and both information panels; Hide returns to the compact
row without discarding data. The expanded header keeps driver/trailer and actions
in the following rows.
The distance uses the selected primary unit (miles for Both), existing retained
progress and a neutral dash when unavailable. Its fixed-width slot stays stable
through loading. Narrow or enlarged-text layouts may place it on a second row
instead of clipping the title or controls. Desktop hides this duplicate distance.
Load/order, total, Remaining, next stop, appointment and ETA follow telemetry,
HOS and current location. Changing presentation does not trigger requests or
recalculation. The entire card is bounded by the map with internal scrolling.
Content and the header
scroll together with the normal scrollbar, including on short screens and with
enlarged text. Do not clip facts to conceal overflow. On phones, place the address
and load link across the full width beneath telemetry and HOS.
Wide bodies top-align GPS beside telemetry and HOS. Current duty and its elapsed
time stay directly below the HOS clocks; omit the static provider label and Next
recap from the truck inspector. Dispatch retains Next recap and detailed rest/reset
countdowns. Route & load details uses an external-link icon at the end of the
truck action row on desktop. Keep its accessible
name, tooltip and disabled slot before the current load identity arrives.
On phones it follows the other icons in the action row.
The retained readings are always visible. On wide inspectors,
load number/order/total, Remaining, next location and timing form four adjacent
columns. The load label and number share a line, with order and total beneath;
do not reserve a separate full-width metadata strip. Give the next-location column
slightly more of the timing column's width for facility names and long streets,
without enlarging the inspector or changing the metadata/distance columns. Intermediate widths put
metadata above the other groups, and narrow screens stack them. Use subtle vertical
dividers between adjacent route groups; stacked groups
use horizontal dividers. Place separators in the existing gaps without reducing
content width or changing the group's text alignment. Use smaller named
vertical gaps around telemetry and HOS without changing their type or dial size.
The tracked next stop owns one retained appointment row above ETA, including its
neutral loading placeholder. Label it Pickup or Delivery from that stop's action;
do not show the final delivery appointment while the truck is heading to a pickup.
Match load-reference appointments by exact stop and dispatch IDs when filling
an appointment missing from a saved preview. A dated plan keeps its own
appointment beside its calculated lateness; a separate details response must
not replace that date. Never move the row under Load when its date arrives.
Keep its label and value inline when space permits, using the same compact gap
as ETA. Do not give Pickup or Delivery a fixed-width label that leaves extra
space after the text. On phones, Pickup/Delivery, date and time share one line
when they fit, otherwise wrapping naturally. Keep AM/PM attached to its clock
when wrapping. Multi-day appointments retain both dates without implying a
single-day window. Desktop keeps the existing inline date/time presentation.
Phone ETA also wraps its label, date and time only when needed, with arrival status
on the line below. The shared stop-hours styles own this layout;
Fleet enables it through the `--stop-hours-road-*`
presentation properties without changing Dispatch or arrival calculations.
Allocate room
for a full appointment window; narrow or enlarged-text layouts may wrap without clipping.
Desktop Remaining uses three left-aligned rows: label, miles, then kilometers.
Mobile groups load metadata and Remaining in equal-width columns, aligning
Remaining's left divider with the appointment divider below. Below them, the stop
address sits beside its appointment, ETA and cycle, with a subtle vertical
divider. Narrow or enlarged-text layouts stack the
metadata and Remaining, with distances in one wrapping row.
Both distance
rows and their units use `body`, matching operational values rather than the smaller
labels. The main number has weight 600; units and secondary kilometers use regular
weight. Do not enlarge the column or use the larger headline-reading size.
Distance to an intermediate next stop belongs inline beside its pickup/delivery
heading, in the selected units. Do not stack a second three-row Next Stop metric
under Remaining; it must not stretch the route panel. Keep that heading mounted
through loading and omit the duplicate distance for the tracked final stop.
Keep route metric columns independent of the currently formatted values. Loading
uses the same load/reference rows and a neutral ETA placeholder, without briefly
showing a second unknown distance or duplicate delivery. Keep the
same retained route panel and group elements through saved-preview, load-reference
and live-forecast reads. Populate those slots instead of switching loading and ready
templates. The neutral ETA/cycle placeholder shares the forecast's row hierarchy;
the desktop truck inspector reserves its scrollbar gutter so overflow cannot change
its responsive column layout. Load, order and total use assigned grid slots even
before their values arrive; narrow layouts keep their three metadata rows instead
of rewrapping the row when an order number replaces a dash. Route options uses the
shared route icon with an accessible name and tooltip; its disabled loading state
occupies the same action slot as the enabled control.
The groups stack their own facts independently; taller cycle forecasts must not
stretch the distance or location columns.
The next-stop address includes its provider-supplied facility/company name above
the street and locality as a body-sized semibold value. Reuse the selected PlanStop name, never
the broker/customer name, and keep address copying limited to the original address.
The truck route summary omits the decorative location pin. Give its address the
full column width and ellipsize a long street to one line. Preserve the complete
street in the DOM, the full address in its native tooltip and clipboard action,
and wrapping city/region/postal information below it.
Omit blank facility names after loading; a cold loading address reserves the same
facility line. Long names wrap without widening the inspector or shrinking text.
Narrow maps wrap those groups without shrinking text or clocks.
Use three named text levels throughout the truck inspector: `small` for labels and
supporting facts, `body` with weight 600 for operational values, and `lead` with
weight 600 for truck identity, load number and headline readings. HOS retains its
component-owned fitted numbers. Metric fuel follows the same reading hierarchy.
Truck header actions retain visible keyboard focus and shared compact metrics.
Telemetry, HOS/current duty and GPS location stay together above load/order,
remaining distance, next location, appointment and ETA. Do not introduce another
disclosure or widen the inspector when data arrives.
Keep the load reference together,
then group route distances, next-stop address/appointment and ETA adjacently.
Do not repeat next-stop distance in the header. When the tracked next stop is the
final route stop, show only Remaining in compact mode; never infer that identity
from equal rounded distances. The appointment is the tracked visit's scheduled
date/time or window, not a different stop's appointment or calculated ETA.
Selecting another truck keeps the same visible panel structure.
Keep Speed, Fuel and Engine in one aligned, content-sized telemetry group.
On mobile, Speed/Fuel and Engine/temperature form two compact rows on the
left, with four HOS clocks in one row on the right. Current duty sits directly
below those clocks. Separate the columns with a subtle vertical divider.
Use 32px mobile telemetry icons and body-sized readings. Mobile HOS uses four
equal columns across its available field; dials grow up to `hos-dial` and shrink
to fit that column. Touch buttons remain 44px. Increased root text
may wrap groups when necessary to retain readable, unclipped information.
Keep telemetry content-sized with small shared gaps. HOS fills the remaining
column without changing its value/label hierarchy.
Hide the separate Fleet Map page heading on mobile. Driver and trailer share
one wrapping header line without the visible Driver label; desktop is unchanged.
Outside temperature uses a weather icon, a short Temp label and one value.
Keep an accessible name and the Google Weather timestamp tooltip. On mobile it
joins the three telemetry readings. Desktop also keeps it in that same row,
with its icon above the value and the same divider as the other readings.
Weather icons use semantic condition colors: warm sun, blue rain, cool snow,
muted clouds and a warning tone for storms. Unknown conditions remain neutral.
Personal temperature choices are Fahrenheit or Celsius, defaulting to Celsius.
An explicit Fahrenheit selection is preserved. Legacy Both and absent
temperature preferences display Celsius; no background preference write is
needed. Distances retain the independent miles/kilometers/both preference. Both
is the distance default; keep Remaining in label/miles/kilometers rows and omit
the secondary row
for a single unit. Keep the temperature cell when unavailable, using a dash and
thermometer icon rather than zero or an unrelated sensor. Fetch current weather
for the selected truck independently from routes and HOS. Do not show current
weather on historical dates. Keep Google's required source attribution visible
in the map key, outside the truck card.
Card updates are immediate, without reveal/fade/resize animations.
Delayed card content must not recenter or zoom the map, even
before the first user map gesture. Update cached overlay insets for the next
intentional focus without treating card growth as a map viewport resize.
Active Follow preserves its screen anchor through delayed
content, including subsequent truck movement frames. Capture that anchor when
Follow starts; refresh it only on explicit reactivation or an actual map resize.
Keep following the truck without adopting content-driven inset changes.
Returning from Dispatch must synchronize new marker layers with the retained map
before revealing them. Do not show an uninitialized marker projection or require
a pan/zoom gesture to correct it. Synchronization must preserve the user's camera.
Expanded truck details use content-driven row heights and compact padding; do not reserve
blank address/ETA rows or enlarge readings just because the viewport is wide.
Both densities use the same `control-touch` HOS diameter through `--hos-dial-size`,
with identical telemetry and driver typography as data arrives.
The inspector header owns the truck number; do not repeat it with a decorative
truck illustration in the expanded body.
Expanded truck inspectors show the selected truck's provider-supplied GPS
address, separate from the always-visible next stop. Omit the GPS date/time row from the
truck card without changing the telemetry timestamp or refresh behavior.
Historical date selections label it Recorded location. Missing addresses are
explicitly unavailable, never replaced with a destination or another truck's last
address. Reuse the existing telemetry refresh; do not reverse-geocode on render.
Clicking the GPS address copies that displayed address using the existing clipboard
action. Keep its plain address appearance without a separate icon or button chrome;
retain native keyboard activation and visible focus. Unavailable addresses are not interactive.
Truck selection retains its road without fitting or zooming out. Show route,
beside Follow, explicitly fits the retained remaining road and turns Follow off.
It does not fetch, republish geometry or recalculate a route. At overview
zoom, nearby unselected trucks share a counted, clickable marker that zooms in;
the selected truck is never clustered and remains larger than surrounding trucks.
The count label sits directly at the group's geographic center with zero screen
offset. Nearby markers, polling and zoom must not push it away from that point.
Only individual truck labels use collision displacement and short connectors.
Clicking the group label jumps to the first zoom
where its members no longer cluster, bounded by the existing individual-marker
zoom threshold, rather than advancing two zoom levels per click.
Selecting a truck group preserves the current truck selection, inspector,
visible panels and displayed route. Stop Follow before exploring the group;
do not select a member or treat its label/anchor as a blank-map click.
Search still selects an individual truck directly, including a clustered one.
At close zoom, individual truck labels avoid other truck labels and symbols.
Keep the truck symbols at their exact geographic positions; displaced labels
use a short connector and remain selectable for that truck. Selected trucks
have placement priority. Retain clear offsets through polling and fractional
zoom changes instead of restacking every frame.
An eligible saved road remains visible when truck progress is unavailable. On a
cold selection, apply saved geometry and available server progress atomically so
the first fit and paint exclude passed road. Without progress show its saved
geometry; after progress has been received retain
the last trimmed road through telemetry gaps. This visual fallback must not report
zero mileage as measured progress or invent ETA, fuel or completed stops.
Selecting, loading or switching content must not change the map element's
bounds. Keep empty selection free of reserved space and bound the overlay's height
with internal scrolling; mobile retains all truck actions and Close. Camera
focus accounts for visible overlays without recalculating routes. Fuel details
reuse existing server prices, quantities and costs, with compact gauges and
side-by-side station, quote and purchases when width permits.
Valid planned fuel markers stay visible and selectable with the route when the
ordinary Fuel Stations layer is off. They reuse server-provided station locations
and quotes without loading every station. Invalidated recommendations stay hidden.
When Fuel Stations is off, retained planned and edited station points keep their
price colors. Visibility controls ordinary stations, not the price palette.
Without loaded station details, use the selected day's lightweight price overview
for the same all-station, per-currency scale and station quotes. Never compare only
planned purchases: the most expensive planned stop can still be globally cheap.
Missing comparison data remains neutral, including absent IFTA quotes. Date changes
clear the old overview before loading its replacement. Do not invent prices or load
the complete station catalog solely to color planned stops.
Opening a fuel inspector loads the selected day's quotes on demand even when the
marker layer is off. Reuse that date's loaded quotes without toggling map visibility.
When next-day quotes exist, compare the selected date with the following date in
content-sized columns. Preserve the blue Your price highlight and green IFTA and
savings values only in the baseline column. The following day's values use the
same favorable/unfavorable/unchanged tones as the change columns, without the
baseline highlight. Deltas are server-provided and only compare matching currencies
and units; missing values remain blank dashes.
Ordinary station cards retain the fuel-quote minimum width, bounded by the
available map width, even without tomorrow's prices. Content may widen them up
to the fuel-card token. Title and Back to truck (or Close without a selected
truck) share one header row when they fit; narrow or enlarged-text layouts may
wrap without clipping. Available next-day prices use the same bounded comparison
layout. Keep both variants centered with the same top-gap rule. Planned fuel
visits retain room for quantities and gauges. On phones, ordinary and planned
station inspectors use the full map width, with or without comparison prices.
Height remains content-driven with the existing scrolling limit.
Quote headers use Yesterday, Today and Tomorrow relative to the local calendar;
other selected days keep their dates. Show server-calculated absolute and percentage
changes side by side. Percentages use the original selected-day value; missing or
nonpositive original values have no percentage. Zero changes retain normal text.
Use subtle vertical dividers before the following-day, absolute-change and
percentage-change columns. A planned station inspector has its own bounded width;
when space permits, place station identity, quote and purchase gauges in three
adjacent groups, with cost/actions in a single footer. Stack by inspector width,
not by the browser viewport alone.
Current and future pickup/delivery details always show the
server-provided estimated fuel on arrival using the shared compact fuel gauge,
with percent inside and its label and US gallons alongside, in the arrival facts
column. Match exact dispatch and stop identities. Invalid or absent fuel plans
show a dash; the Fuel Stations marker layer does not control arrival visibility.
Current and future route-stop inspectors share a compact, bounded width, keeping
location and arrival information adjacent instead of stretching across the map.
Future-stop details show the exact stop's imported PU # or DEL # below its address
when available. The value copies on click and remains distinct from Appt # and the
load's order number; missing or mismatched stop details never supply a reference.
Next load stop starts compact, retaining load/order, stop identity, company,
address, appointment, fuel on arrival and ETA. Its header's stable Details button
reveals assignments, customer, stop/appointment references, distances and cycle
forecasts. Keep the same summary and width in both states. Disclosure is local
presentation only: no requests, recalculation or camera movement. Loading and
polling retain the chosen state; another stop or a new inspection starts compact.
On phones, keep title and Details in the first row and Back below them.
At very narrow or enlarged-text widths, let this header scroll with the card
instead of covering the stop facts with a tall sticky area.

## Dispatch information hierarchy

The [Dispatch load workspace](features/dispatch-workspace.md) supersedes the
former board modal-opening workflow. Cards, Table and Papers use native links
to `/dispatch/{id}`, matching Fleet Map. Exact-stop links add `stopId`; modified
clicks retain normal browser behavior. The full page, not a popup, owns load
editing, broker contacts, documents and the internal dispatcher journal.
The route uses a compact stop table above the selected-stop editor on the left.
Notes, Documents and route/mileage share the independently scrolling right column.
Rows show number, operation, completion, facility/locality, appointment,
truck/trailer/driver and ETA. Give stop identity more width than ETA; let full
ETA dates wrap in their compact column. Place the stop count beside the location
heading and the accessible Add stop icon in the table header, without a separate
toolbar row. Narrow layouts retain the count and add action in a compact header.
A separate editor below the table shows that exact
visit, without inserting content between rows or collapsing on repeated
selection. Reordering remains
a draft until the single Save changes action; Discard restores the opened draft.
Keep load identity, status, order, broker and rate in one wrapping header
row, with semibold values and subtle separators. Omit the copy-link action,
managed-ownership badge and routine import/source
strip and duplicate draft notices; preserve the header save state and actionable
conflict warnings. Removing explanatory text does not relax edit guards.
Do not mount the legacy Truck starting stop editor below the route workspace;
resource editing belongs to the selected stop. Existing planning overrides are
retained and are not cleared by this presentation change.
The New load page reuses the stop workspace, address search and shared form
controls. Its single Create load action preserves uncertain submission identity.
When no supporting or route content exists, the stop workspace uses its full
width and exposes only Stops and Details on mobile.

The full-page workspace fits the app viewport. On desktop the right information
column scrolls independently alongside the route/editor panes. Narrow screens
use Stops, Details, Notes & files and Route buttons to show one scrolling pane
at a time, without document scrolling. Selecting a stop opens Details; switching
panes retains mounted components, drafts and uploads.
Bound the workspace to the viewport. Its stop table, selected-stop editor and
supporting column scroll vertically and independently. The table
uses dispatch-stop-list-compact and 30% as height caps so editing has priority;
table headers stay outside their scroll regions. The selected-stop editor omits
the duplicate identity/address banner and retains a screen-reader-only heading.
Selecting a
different stop resets only the editor's scroll, not the itinerary or page.
Reveal a newly selected row with the smallest vertical scroll of the table alone;
align oversized rows at their start. Repeated selection and unrelated rerenders must
not recenter it. Use operation icons and semantic pickup/delivery colors, quiet
completed-row numbers and a clear selected row/editor accent. The accessible
editor heading names the selected stop and facility. Group direct resource
controls on a soft surface; keep field groups visible and document upload compact.
Do not repeat a disabled stop-type field for an existing stop; retain its operation
action and the type selector when creating a new stop. When resource options load,
retain the selected resource identity rather than displaying the first option.
Do not show repeated-visit counters or up/down buttons in this workspace.
The shared drag handle also supports selecting a destination by tap and Up/Down
keys (with Left/Right retained for compatibility),
preserving accessible reordering without extra visible controls.
While dragging, dim the source and show a themed insertion line and target
highlight at the exact before/after position used on drop. Invalid boundaries do
not show an insertion target; previewing a target must not reorder the draft.
Show protected visits as compact saved facts, not long disabled forms or
unusable reorder controls. Keep exact references, repeated-visit context,
recorded events and instructions accessible. Resource snapshots appear in
the route summary and above the selected stop's editable fields.
The selected stop editor has no duplicate cycle forecast or separate Stop updates
panel. Status and operation actions belong inside that exact editor. Status drafts
survive stop selection, and the list previews completion through the selected
stop until Save or Discard. Direct
resource selectors share a compact row. Truck, trailer, driver and co-driver selectors use local, case-insensitive
comboboxes for unit numbers or names, without a search icon. Rank exact and prefix
matches first; show up to ten results and prompt users to narrow larger lists.
Only selecting an option changes its ID. Escape or blur restores the saved
label without assigning arbitrary search text. Location, appointment and references
occupy the left field column, with contact and cargo on the right. Instructions
remain editable with references. Use a flat two-column form, subtle vertical
dividers and shared icons rather than nested field cards. Narrow containers
stack the fields without shrinking controls. An active contextual transfer
editor uses the editing pane while the ordinary form stays mounted but hidden;
closing the transfer restores that same form. Bound the left pane so its
contents cannot overlap the right column at enlarged text sizes.
The map reads saved, input-matched road geometry for all noncancelled execution
sections, including completed sections. Keep gaps between sections separate;
never substitute straight connecting lines or calculate mileage in the Client.
Hide saved roads while route edits are unsaved. Opening the map must not call
the routing provider. Road-route navigation
uses the current native truck identity, not a historical stop assignment.
The stop map zooms directly with the wheel and hides camera/zoom controls.
Co-located stops share a marker containing each stop number; selecting a stop
highlights only its own number without moving the geographic point.
The right column starts with the map, followed by Notes and Documents, then
mileage and load instructions. The map stays in the Route pane on mobile.
Map/mileage, notes, documents and contextual transfer components remain mounted
after saves for the same load. Only changed map coordinates rebuild markers;
ordinary stop selection highlights its number and fits the complete route in
road-map mode. Repeated single clicks on the same stop toggle between the full
route and that stop at zoom 18 in satellite mode. Selecting a different stop
starts with the full route. Background updates do not repeat these explicit
camera actions.
Missing configuration or coordinates must show an honest unavailable state and
retain the server-provided mileage and Fleet Map link.
Reuse ActionIcon for location, appointment, contacts, references and cargo.
Address lookup uses an explicit Google search through the authenticated server
endpoint. Selecting its result fills the address and coordinates; latitude and
longitude are not editable UI fields. Typing must not issue provider requests or
apply results from an older query. A lookup alone does not change the saved load.
Explicit native trailer transfers show separate Drop and Hook actions with
their releasing/receiving resources and confirmation state. Shared locations
must not imply a transfer or completed event. Source provenance identifies
the provider and latest import, not a successful live connection check.
Keep native resource-change and actual-stop controls below the selected editor
in Overview, not in a separate Assignments tab. Stop summaries name the truck,
trailer and driver, with wrapping names and a dash for missing assignments.
Notes use one load-wide field and Add note; existing
notes and unresolved issues retain their history. Documents upload immediately
when chosen or dropped in Overview, with progress and explicit failure retry.
Keep unsaved drafts through section changes and guard navigation away.
Keep broker contacts and load pricing together under Broker & billing, not in
Overview. Order the tabs Broker & billing, Overview, History; open Overview by
default. Their controls share the load draft and Save changes action; switching
sections preserves values and does not fetch them again.
Place section navigation beside Back to Dispatch in the existing top header,
wrapping when needed rather than reserving another full-width tab row.
Ordinary stop operation fields are directly visible in the selected editor,
without an opening button. Do not show a Trailer after stop selector. Preserve
recorded state when opening the editor; never infer empty cargo after a partial
delivery. Completion uses an accessible pressed-state button, with
the same selected action colors as map toggles. In the full load page, operation
and correction drafts use the header Save changes and Discard actions without
duplicate local buttons. Shared standalone editors retain their own actions. Preserve
the explicit imported-operation reset. Operation drafts guard competing edits
and navigation, and save through the existing revision-checked operation command.
The page uses the shared Pulse brand, semantic tokens and control metrics in
light and dark themes. Narrow layouts switch panes without shrinking text.
The shared legacy load-dialog components remain reusable presentations; board
navigation must not mount or open them.

Shared distance text keeps each number attached to its unit with a nonbreaking
space. Both-unit values may wrap between miles and kilometers, never before an
isolated `mi` or `km`. Preserve the selected units and existing font hierarchy.

The load route editor is a compact top-centered card on the existing Fleet Map.
Its top gap is capped by actual side clearance and is zero at full width. The
body scrolls independently; header and Save/Cancel remain visible. Mobile keeps
part of the map uncovered for adding points. Route cards and road lines share
map-series colors, with the selected option blue. Editing temporarily hides
ordinary roads, fuel markers and other trucks without discarding their state.
Cancel restores them; only an explicit save changes the selected road. Show
driving time separately from ETA and make the need to recalculate fuel clear.
For current-road options, show the server's GPS origin time and Remaining stops
only caption. Compare Saved remaining route with the preview, not a full-load
distance. Render only the returned one, two or three options; never pad the list.

Cards use a compact truck header and horizontal current/next/upcoming load lanes
on wide screens, with vertical pickup/delivery timelines inside each card. Keep
the same page heading and filter toolbar outside the view-specific body frame
for Cards, Table and Papers; switching views must not move that shared top area.
Keep horizontal cards and their footers aligned to the tallest content-driven card in
their row; do not reserve a fixed height. Stacked mobile cards keep independent
heights. Completed visits remain visible as short numbered rows with a completion
check, city and scheduled appointment; full addresses and visit context remain in
Details. Repeated after-state labels do not belong in compact cards. A single load
keeps the same lane width as cards beside other loads, never stretching across the
board. When an assigned truck has only a current load, show a compact No next load
placeholder in the next lane; stack it below on phones. Do not use that placeholder
for loading, failed, completed or next-only lists. Wide cards place completed history
beside remaining stops, while narrow cards stack them. The compact footer keeps remaining
miles and the fuel-stop count. Loaded, empty and total miles, rate and both
server-provided rate-per-mile values belong in Details, not a permanent card strip.
Street addresses and ETA stay in the compact stop timeline. References,
facility details, editing and cycle forecasts belong in the full load workspace,
not inline board-card accordions. The workspace retains an ordered stop list
with the selected stop's editor in a separate region below it. Selection follows
the exact
stop ID across refreshes and clears when the stop or load identity is removed.
Supplemental facts wrap in compact label/value groups on a quiet surface, with
notes below. Cargo fields are hidden for driver-only travel and explicit
non-cargo operations; pickup/delivery retain cargo facts, including an unloading
stop whose after-state is Empty. Merely opening details never changes imports.

The retained `DispatchLoadDialog` is a compatibility presentation, not the board
or Fleet Map load-link destination. Its overview places stops side by side when
width permits. More details opens the selected stop at full width inside that
same native dialog, without nested popups. All stops restores overview scroll
and opener focus. Keep its height stable with internal scrolling for long facts.
Resting operation/completion buttons share a wrapping footer; open editors use
its width. Do not show empty More details actions. These dialog controls and
modal focus behavior do not apply to the full-page workspace.

Within a wide load card, each stop places its location/address on the left and
appointment/ETA on the right. Narrow cards stack those facts without shrinking
the type or hiding stops. Facility names do not add another summary row when the
street is present; they remain in Details and on the location's title.
Loads with more than two stops show a compact total with pickup/delivery counts.
Repeated visits to the same complete address retain separate numbered timeline
entries and appointments, with `Visit 1 of 3` context on Cards and in Details.
The same visit context appears in map stop inspectors; a load-stop position is
distinct from the map's remaining-route marker number. Never combine provider
stops solely because they share a facility or map point.
Table cells summarize each pickup/other or delivery group with its first unfinished
visit, or its last visit when all are completed. Multi-stop groups show total and
completed counts in a native link to the full load page.
Keep the selected visit's identity, appointment and completion visible; all visits,
addresses and facilities remain available in that workspace. Omit the redundant
After: Loaded line from table cells; the underlying stop state remains available
in Details. Do not stack the full
itinerary into a table row or shrink text to accommodate it.
Papers groups eligible pickup/delivery appointments for today and tomorrow in
one column, ordered by the displayed appointment date and time. Use the local
calendar date, not a rolling 48-hour window. Keep planned loads in Awaiting
pickup and picked-up loads in In transit, even when their dates fall in that
window. The completed archive remains separate.
Cards, Table and Papers navigate to the same full load page. Its workspace
read owns editing metadata and server forecasts. It does not recalculate routes
or mutate business data merely by opening. Back to Dispatch uses normal page
navigation, guarded when any local draft would be lost. Copy feedback stays inside the order
button as an icon; successful copies must not add a status row.

Truck-start confirmation belongs in Details through the shared
`TruckAssignmentEditor`: select the truck and exact visit, confirm explicitly,
or return to imported assignments. Earlier stops show `Driver only · No truck`
without a missing truck ETA or implied completion. Map markers resolve details
by stop ID, not by an index into the full driver itinerary.

`StopOperationEditor` belongs in each stop's Details and the full load page.
Keep Action and After this stop distinct, use explicit confirmation and a return
to imported operation, and never infer empty cargo from missing fields. Dispatch
and map stop labels display the effective action and server-provided after-state;
equipment operations use a neutral stop tone rather than delivery coloring.

Manual stop completion belongs in that stop's Details, not on every compact card.
The full load workspace exposes status, truck, trailer, driver and co-driver
directly for an ordinary selected stop, including completed stops. Semibold labels
and shared icons distinguish editable facts from regular-weight hints. No opening
button, assignment checkbox, reason or actual-time entry is required.
Only changed resources are applied; unchanged resources stay assigned independently
on each section. Assignment changes reveal the scope and exact affected stop numbers:
current assignment, entire load, onward or a selected range. Scopes must cover
whole existing assignment legs; partial-leg ranges are explicitly rejected rather
than silently changing earlier stops. Source-only loads have one whole-load scope.
Status changes affect only the selected stop. One Save commits the correction and
automatically records the actor and history; Discard restores saved values in the
full load page, while standalone editors use Cancel.
A changed draft locks competing load/stop mutations and protects navigation until
Save or Discard (Cancel in standalone editors). Merely viewing a stop does not lock it. A submitted uncertain request retains
its retry identity. Transfer stops expose their resources directly as well.
Truck/driver changes apply to the selected side. A trailer correction on a
Drop/Hook updates both linked assignments and the same custody record atomically,
without changing recorded confirmations or their timestamps. Connected additional
transfers, shared loads and overlapping custody require a wider coordinated
correction and are not silently rewritten. Unconfirmed transfers retain their
explicit confirmation workflow; resource edits never confirm a transfer.
Use the shared `StopCompletionEditor` with named control tokens, wrapping date/time
fields and explicit confirmation/cancel actions. Show the manual actor and actual
time separately from provider facts; undo must say whether provider completion
will remain. Opening, validating invalid input or cancelling must not send writes.

The next load uses the named `route-next` and `route-next-surface` roles, while the
current card retains its blue outline. These card roles do not change fuel-price
or map marker colors. Table and Papers retain the same financial data and
completed-stop information. Table rows offer a native Open load link; map links and modified clicks retain
their separate behavior. Papers opens the full load page after explicit
selection, never during a background refresh.

Truck status and fuel belong together in a compact telemetry group. Reuse
`FuelReading` for its pump icon, percentage and soft warning/success surface;
the selected Fleet Map header uses its `metric` variant to align with Speed and
Engine, while Dispatch keeps the compact pill. Speed units inherit their number's
font size, using regular weight beside the semibold number. These telemetry colors must not
recolor fuel-station price markers.
Fuel icons are yellow at 30% or below and warm orange at 15% or below. Speed icons are
green through 65 mph, yellow above 65 through 70 mph, and warm orange above 70 mph.
Engine icons are green while moving, yellow while idling, and neutral when off
or unavailable. Values and labels retain their normal readable text colors.
Dispatch's Driving pill uses the same speed thresholds, including trucks without
an assigned load. This does not change the green moving/Idle map markers.
Duty details use their content height; do not reserve empty lines above Next recap.
Truck action icons use one wrapping row rather than a forced two-column grid.
The truck's single pump action opens Fuel plan for both automatic and manual
plans, including an empty plan. Do not show a separate fuel-edit pencil or start
an automatic calculation from that action. Calculate automatically starts the
replacement immediately on one click, without a separate confirmation. Disable
repeat submission while it runs; failed calculations retain the saved plan and
draft. Keep the opened version token and truck/dispatch ownership checks.
If the edit preview is unavailable, keep automatic calculation accessible after
loading; its server-checked version comes from the opened plan and must not follow
later polling updates.
The metric Fuel reading shares Speed/Engine icon size, label font, row gap and
value line height; the compact Dispatch pill retains its existing appearance.
Fleet Map uses the named `telemetry-icon` size for those three icons without
enlarging their labels or values. Metric Fuel accepts `--fuel-reading-icon-size`
from its owner and otherwise keeps its existing heading-sized icon.
Dispatch headers left-pack truck identity, telemetry with route distances, and HOS
clocks in adjacent content-sized groups. Keep normal named gaps and the map action
beside identity; unused desktop width stays to the right, not between groups.
Duty/rest and Next recap remain below identity and telemetry. Narrow screens and
enlarged text wrap without hiding information or stretching these groups apart.
Dispatch keeps HOS clocks, Next recap, current duty/rest and route distances
visible without accordions. Fleet Map also keeps HOS clocks and current duty
visible above load information. Detailed stop forecasts remain in the workspace.
The sidebar account uses the current authentication claims, not illustrative names.
The account name/role toggles a compact disclosure containing Personal settings
and Logout, hidden initially. Both entries use shared sidebar button metrics.
Personal settings closes the disclosure and mobile navigation when selected.
Escape closes the disclosure and returns focus to its trigger.
Authenticated visitors to Login go directly to Fleet Map with history replacement;
unverified sessions retain the sign-in form without treating token presence as proof
of authentication.
Display clock times in the 12-hour format with leading-zero hours and AM/PM
(for example, 02:00 PM), preserving each value's existing timezone and date.
HOS clocks and other durations remain elapsed/remaining hours and minutes, never
AM/PM. HOS value text scales with the dial diameter so two-digit hours fit inside
the ring without changing fuel-gauge typography.

## Control behavior

Main navigation exposes Customers & brokers as one shared company directory.
One Admin-only Fleet entry opens Trucks by default and stays active across the
Trucks, Trailers and Drivers tabs inside the existing configuration pages. Fleet
Map remains separate; do not duplicate these tabs in main navigation, relax
permissions or duplicate resource identities. Settings uses
exact route matching so a resource page does not highlight two menu entries.
Settings does not repeat the Trucks, Trailers and Drivers navigation; these tabs
belong only in Fleet.
The company directory searches explicitly by name (at least two characters, up to
20 matches) and reuses the existing normalized identity and revision-checked
profile endpoint. Shared billing and Quick Pay fields belong to BrokerTerms under
Shared/Customers. Company edits do not retroactively update saved load terms or
calculate driver pay. Preserve drafts on failed saves and guard navigation.
Generic Transfer yard captions use the stop's available address; real facility
names remain unchanged. This is presentation only, not removal of a Drop/Hook,
renaming a saved location or relaxing confirmed-transfer correction rules.

Main-layout page navigation uses a short 180ms opacity entrance on its existing
content surface. The sidebar stays still. Do not delay navigation, retain the old
page, add animation libraries, remount content or move/resize the map for motion.
Only a changed URL path restarts the effect: loading, polling, disclosures,
query strings and fragments do not. Respect `prefers-reduced-motion` by omitting
the animation entirely. Initial loading remains immediate without a late fade
when shared settings arrive.

Fleet Map places its heading and toolbar in one wrapping row. Search grows into
available width beside the date and separated Map layers group. Desktop layer
controls use compact icons with native title hints and explicit accessible names;
mobile Filters retains visible labels. Keep shared button metrics, filled
action/on-accent roles when enabled, native checkbox semantics and visible
keyboard focus. The date keeps its accessible label without desktop label text.
IFTA stays readable as a short label
in its own fuel-price group immediately before Fuel Stations, with the same chip
appearance and visually hidden native checkbox. Its state is separate from layer visibility. Search counts are
announced without inserting toolbar width. On phones, Filters opens the retained
date/layers/price controls in an overlay below search, without resizing the map.
Controls wrap at larger text sizes instead of shrinking text or touch targets.

Remember Traffic, Next loads, Fuel Stations and IFTA per account in the current
browser. Restore them before starting map layers; hold the controls disabled
during restoration and map initialization. Reserve the layer controls' geometry
but hide their unconfirmed state until restoration completes, then reveal the
saved choices without waiting for the map provider. Missing, invalid or blocked
storage uses the defaults: Traffic on, the other three off. Storage failure must not
disable map interaction. Save only explicit toggle changes, not polling or
temporary editor visibility. Do not retain the date, search or selected truck.

Popup, compatibility load dialog, camera dialog, route/fuel editor and map
inspector headings use the shared `ui.overlay-title` mixin: lead text, weight 600
and line height 1.4. The full-page load workspace uses page heading semantics.
Dismiss controls use `ui.icon-control`, including keyboard focus and touch sizing.
Widths and scrolling remain content-specific; modal backdrops are not added to
non-modal map inspectors or editors.

Fleet Map displays only one information or editing overlay at a time. Opening
Fuel plan, Route options or the camera hides the retained inspector; marker clicks
and delayed updates cannot reopen it over the active window. Closing the window
returns to the selected truck. Cancelling Fuel plan also fits that truck's
retained remaining route after the inspector is restored, without fetching,
recalculating or including passed roads. A later selection, editor or disposal
cancels the queued camera return. Other overlay closures do not refit the map.
Camera dialogs are mounted
outside the hidden inspector. Opening another editor must not discard an active draft.

Control metrics belong in `Client/Styles/base/_variables.scss`. Reuse `ui.input-control`, `ui.button-control`, `ui.compact-control`, `ui.checkbox-control` and `ui.invalid-control` through the public `base` module; page styles should only set placement and width, not recreate borders, padding, typography or focus states.

Form wrappers, settings inputs, dispatch filters and map search/date fields use the same input mixin. Standard controls are 40px high with 14px text, 8px corners and 8px/12px padding. Compact action buttons are 32px; below 800px all action buttons and fields have a minimum 44px touch target, and fields use 16px text.

Use `.btn` for secondary actions, `.btn--primary` for save/submit, `.btn--danger` for destructive confirmation, `.btn--table-danger` for a table's delete action, and `.btn--text` for low-emphasis actions. `.btn--small` and `.btn--table` share compact sizing. Pagination, dispatch view selectors and map filter chips reuse the button mixin. Map markers, address-copy surfaces, navigation and document tabs retain their specialized presentation.

Validation uses `.invalid`, `[aria-invalid="true"]` or `.form-field__control--error`; all share the same error border and focus ring. Disabled inputs and buttons retain their native disabled semantics. Checkbox dimensions/accent come from the checkbox mixin.

Send plan is one of the Fleet Map overlays, beside Fuel in the truck actions,
and it hides the inspector like the fuel editor. It shows the hand-over state,
a critical warning when a stop is out of reach, the Ready for current shift
stops with Sent or Changed since sent, a note that later stops stay
provisional, and the message. It says plainly that automatic sending is not
connected: Copy message records nothing, and Mark as sent is the explicit
confirmation. A sent visit shows the same one-word label beside its name in the
station popup; nothing else on the map changes. Fuel plan sending in Settings
is off by default and says that turning it on sends nothing until a channel
exists.

`Shared/Drivers/DriverContactEditor` edits a driver's phone, email and
WhatsApp number wherever a driver's contacts are needed. Each sourced field
has its own "Use source value" checkbox and shows the source value; the
WhatsApp number is never filled from the phone without the dispatcher
pressing "Use the phone number". A refused save keeps the draft.

## Background refresh

Keep eligible last complete data visible during background refresh without internal
lifecycle messages or maintenance explanations. On first load without a saved result,
use a compact neutral placeholder. Never retain unsafe recommendations after their
inputs are invalidated. Show errors only when user action is required.
Use the shared `LoadingIndicator` pulse for pending reads instead of visible
Loading sentences. Keep its accessible status and reduced-motion behavior.
Background map reads place it in a reserved header slot, not a temporary row
that moves the retained facts.
Technical Calculate Fuel failure details belong in the browser console, not a
user-facing detailed error popup. Retain the saved plan on a failed calculation.

Verified the compiled CSS in Chromium using representative DOM wrappers for Users/Login, Settings, Dispatch and Fleet Map: text/select/number/search/date controls and normal/primary/danger/pagination buttons share metrics. At 390px viewport width, fields and buttons measure 44px high and fields use 16px text. These checks cover shared control styling, not an authenticated end-to-end review of every page.

## Address suggestions

`Shared/Search/AddressAutocomplete` owns the debounced address combobox shared
by Dispatch stop editing and Shipment/Border parties. Start after three
characters and 300 ms; cancel older work and ignore late replies. Matching saved
addresses precede Google Maps predictions and retain distinct source labels.
Arrow keys select an option; Enter applies it and Escape closes suggestions.
Applying a party address preserves legal/contact identity and clears the old
unit line. Stop selections still use the existing coordinate verification.
Neither searching nor choosing an address saves the owning form.

Address suggestions, loading and empty/error states share an absolutely
positioned dropdown under the input, with bounded internal scrolling. Opening
it must not move the form below. Show available ZIP/postal codes per result and
one Google Maps attribution footer, not repeated provider text in each row.

## Traveled route context

Fleet Map keeps the full current road visible. A solid, muted stroke remains
under the bright remaining route, and route fitting includes both portions.
GPS progress, mileage, fuel and ETA still use the remaining calculation route.
For GPS-origin reroutes, an available validated full reference supplies the
prefix up to the new origin. This is planned-road context, not a GPS travel log.
Unmatched reference geometry stays visible as a separate muted line. It must
not be joined to a guessed truck position.

Progress updates must replace a multi-vertex remaining-route prefix in one write.
Do not remove its vertices individually: the rendering adapter copies the tail
on each mutation, making a large GPS jump quadratic and blocking map gestures.
Movement within a segment preserves the long road and traveled-context buffers.

Current-route markers retain all available reference stops, including passed
stops. Number 1 is the route origin; completing a stop does not renumber later
markers. Future-load numbering starts after the complete current stop list.

Current empty road uses the deadhead orange; loaded road remains blue.
The GPS approach to the first pickup is empty. Later legs use the preceding
stop cargo state, including reference stops, so another LTL pickup does not
imply an empty truck. Passed empty legs retain a muted orange stroke.

Off-route recalculation reconstructs road from the last passed stop to the new
GPS origin, then joins the selected remaining road. Earlier stop-to-stop legs
stay intact. This display reference is saved with the plan; reads and GPS
polling do not request extra routing. Reconstructed road is planned context,
not recorded GPS history, and does not enter remaining fuel or ETA totals.

Fuel markers retain a reachable first purchase below reserve with a server
warning. If the priced station cannot be reached, show its server-calculated
fuel deficit as an access warning. Such a marker has no purchase amount, fuel
gauges, cost or edit-plan action, and is not a feasible fuel recommendation.

Saved fuel stations remain visible independently of the Next loads road layer.
When the same assignment's fuel plan needs updating, retain its station markers
with a saved-plan warning. Suppress unverified distance, arrival time, purchase
quantity, fuel gauges, cost and edit action until a validated plan replaces it.
Changed route assignment inputs do not retain the previous assignment's markers.
