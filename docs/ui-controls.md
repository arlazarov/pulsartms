# Shared UI controls

## Component ownership

`Components/` owns generic controls such as tables, forms and popups. `Shared/`
owns reusable application UI, grouped by responsibility: `DriverStatus`, `Fuel`,
`Dispatch`, `Search` and `Trucks`. A Razor/code-behind pair lives in a component
folder inside its group, for example
`Shared/DriverStatus/DriverHours/DriverHours.razor` and `DriverHours.razor.cs`.
Standalone helpers stay in their owning group; they do not need one-file folders.
Code-behind namespaces follow their folders, and Razor imports expose the
component namespaces.

`Pages/<feature>/` owns route components, page coordination and components used
only within that feature. A component used by multiple features belongs in the
appropriate shared group, together with its reusable presentation helpers.
Shared UI, models and services must not import page namespaces. DTOs live under
`Models/DTO/` in their feature folders and must not depend on UI components.
For example, both Fleet Map and Dispatch use `Shared/DriverStatus/DriverHours`,
while its input model is `Models/DTO/Planning/DriverHosClocks`.

Shared load dialogs and their stop/cycle children live together under
`Shared/Dispatch/`. Page-local load cards, tables, paper views and map-selection
components remain with their pages. Moving reusable UI must not move polling,
selection or request ownership out of the page that controls it.

## Tokens and themes

Primitive palette values live in `base/_colors.scss`; semantic theme maps live in
`base/_themes.scss`. Import `@use '../base' as ui` from UI styles. Use `ui.theme(surface)`,
`ui.theme(text)`, `ui.theme(border)` and other semantic roles for interface colors;
use `ui.clr()` for intentional fixed palette colors such as chart series.
Do not add hex/RGB literals in page, layout or component styles.
`data-theme="dark"` and `data-theme="light"` override semantic CSS properties.
The dark palette is an initial theme foundation, not a completed visual audit of
maps, document previews or provider-rendered content.

Use named `ui.pg(sm)`, `ui.pg(md)`, `ui.pg(section)` spacing and
`ui.fs(body)`, `ui.fs(small)`, `ui.fs(title)` typography tokens. Their values are
defined in `_variables.scss` and exported as rem-based CSS custom properties.
Numeric font-scale calls remain compatible; numeric spacing calls are rejected. Names are validated at
compile time. New reusable sizes belong in the token maps, not repeated literals.
Pixel values remain appropriate for hairline borders, exact map geometry and
viewport breakpoints; do not replace them with unrelated spacing tokens.

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
While editing fuel, show only the edited truck, respecting the Trucks layer toggle;
restore normal truck visibility when editing ends, including empty drafts.
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
Back restores truck information without clearing its route; Close hides only the
inspector. Selecting, loading or switching content must not change the map element's
bounds. Keep empty selection free of reserved space and bound the overlay's height
with internal scrolling; mobile retains an accessible truck summary toggle. Camera
focus accounts for visible overlays without recalculating routes. Fuel details
reuse existing server prices, quantities and costs, with compact gauges and
side-by-side station, quote and purchases when width permits.
Valid planned fuel markers stay visible and selectable with the route when the
ordinary Fuel Stations layer is off. They reuse server-provided station locations
and quotes without loading every station. Invalidated recommendations stay hidden.
When Fuel Stations is off, retained planned and edited station points use the
neutral marker color, regardless of whether price data was previously loaded.
Enabling the layer restores price colors.
Opening a fuel inspector loads the selected day's quotes on demand even when the
marker layer is off. Reuse that date's loaded quotes without toggling map visibility.
When next-day quotes exist, compare the selected date with the following date in
content-sized columns. Preserve the blue Your price highlight and green IFTA and
savings values only in the baseline column. The following day's values use the
same favorable/unfavorable/unchanged tones as the change columns, without the
baseline highlight. Deltas are server-provided and only compare matching currencies
and units; missing values remain blank dashes.
An ordinary station's inspector fits its content between the fuel-quote minimum
and fuel-card maximum size tokens, bounded by the available map width,
without reserving the truck inspector's width. Keep it centered with the same
top gap; planned fuel visits retain room for quantities and gauges.
On phones, both ordinary and planned station inspectors use the full map width;
their height remains content-driven with the existing scrolling limit.
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

## Dispatch information hierarchy

Cards use a compact truck header and horizontal current/next/upcoming load lanes
on wide screens, with vertical pickup/delivery timelines inside each card. Keep
the same page heading and filter toolbar outside the view-specific body frame
for Cards, Table and Papers; switching views must not move that shared top area.
Keep horizontal cards and their footers aligned to the tallest content-driven card in
their row; do not reserve a fixed height. Stacked mobile cards keep independent
heights. Completed pickups remain visible. The compact footer keeps remaining
miles and the fuel-stop count. Loaded, empty and total miles, rate and both
server-provided rate-per-mile values belong in Details, not a permanent card strip.
Street addresses and ETA stay in the compact stop timeline. References, facility
details and cycle forecasts belong in the shared `DispatchLoadDialog`, not
inline card accordions. The dialog places stops side by side when width permits;
street, facility, appointment and ETA remain visible, with supplemental references,
hours and cargo details available through each stop's native disclosure.
Papers moves picked-up loads to In transit even if pickup or delivery is today.
Cards, Table and Papers open the same data-only native modal. Opening it must not
fetch or recalculate business data. Keep the same load identity across refreshes;
Escape, Close and a deliberate backdrop click dismiss it and restore focus without
scrolling or expanding the underlying board. Copy feedback stays inside the order
button as an icon; successful copies must not add a status row.

The next load uses the named `route-next` and `route-next-surface` roles, while the
current card retains its blue outline. These card roles do not change fuel-price
or map marker colors. Table and Papers retain the same financial data and
completed-stop information. Table rows offer a native Open load button; map links
and modified clicks retain their separate behavior. Papers opens its document in
the same popup, only after explicit selection, never during a background refresh.

Truck status and fuel belong together in a compact telemetry group. Reuse
`FuelReading` for its pump icon, percentage and soft warning/success surface;
the selected Fleet Map header uses its `metric` variant to align with Speed and
Engine, while Dispatch keeps the compact pill. These telemetry colors must not
recolor fuel-station price markers.
Fuel icons are yellow at 30% or below and warm orange at 15% or below. Speed icons are
green through 65 mph, yellow above 65 through 70 mph, and warm orange above 70 mph.
Engine icons are green while moving, yellow while idling, and neutral when off
or unavailable. Values and labels retain their normal readable text colors.
Dispatch's Driving pill uses the same speed thresholds, including trucks without
an assigned load. This does not change the green moving/Idle map markers.
Duty details use their content height; do not reserve empty lines above Next recap.
Truck action icons use one wrapping row rather than a forced two-column grid.
The metric Fuel reading shares Speed/Engine icon size, label font, row gap and
value line height; the compact Dispatch pill retains its existing appearance.
Dispatch headers left-pack truck identity, telemetry with route distances, and HOS
clocks in adjacent content-sized groups. Keep normal named gaps and the map action
beside identity; unused desktop width stays to the right, not between groups.
Duty/rest and Next recap remain below identity and telemetry. Narrow screens and
enlarged text wrap without hiding information or stretching these groups apart.
Keep HOS clocks, Next recap, current duty/rest and route distances visible without
driver or route accordions. Detailed stop forecasts belong in the load popup.
The sidebar account uses the current authentication claims, not illustrative names.
The account name/role toggles a compact disclosure containing Logout, which is
hidden initially. Escape closes the disclosure and returns focus to its trigger.
Authenticated visitors to Login go directly to Fleet Map with history replacement;
unverified sessions retain the sign-in form without treating token presence as proof
of authentication.
Display clock times in the 12-hour format with leading-zero hours and AM/PM
(for example, 02:00 PM), preserving each value's existing timezone and date.
HOS clocks and other durations remain elapsed/remaining hours and minutes, never
AM/PM. HOS value text scales with the dial diameter so two-digit hours fit inside
the ring without changing fuel-gauge typography.

## Control behavior

Control metrics belong in `Client/Styles/base/_variables.scss`. Reuse `ui.input-control`, `ui.button-control`, `ui.compact-control`, `ui.checkbox-control` and `ui.invalid-control` through the public `base` module; page styles should only set placement and width, not recreate borders, padding, typography or focus states.

Form wrappers, settings inputs, dispatch filters and map search/date fields use the same input mixin. Standard controls are 40px high with 14px text, 8px corners and 8px/12px padding. Compact action buttons are 32px; below 800px all action buttons and fields have a minimum 44px touch target, and fields use 16px text.

Use `.btn` for secondary actions, `.btn--primary` for save/submit, `.btn--danger` for destructive confirmation, `.btn--table-danger` for a table's delete action, and `.btn--text` for low-emphasis actions. `.btn--small` and `.btn--table` share compact sizing. Pagination, dispatch view selectors and map filter chips reuse the button mixin. Map markers, address-copy surfaces, navigation and document tabs retain their specialized presentation.

Validation uses `.invalid`, `[aria-invalid="true"]` or `.form-field__control--error`; all share the same error border and focus ring. Disabled inputs and buttons retain their native disabled semantics. Checkbox dimensions/accent come from the checkbox mixin.

## Background refresh

Keep eligible last complete data visible during background refresh without internal
lifecycle messages or maintenance explanations. On first load without a saved result,
use a compact neutral placeholder. Never retain unsafe recommendations after their
inputs are invalidated. Show errors only when user action is required.
Technical Calculate Fuel failure details belong in the browser console, not a
user-facing detailed error popup. Retain the saved plan on a failed calculation.

Verified the compiled CSS in Chromium using representative DOM wrappers for Users/Login, Settings, Dispatch and Fleet Map: text/select/number/search/date controls and normal/primary/danger/pagination buttons share metrics. At 390px viewport width, fields and buttons measure 44px high and fields use 16px text. These checks cover shared control styling, not an authenticated end-to-end review of every page.
