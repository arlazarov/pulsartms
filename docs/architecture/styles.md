# Style architecture

## Ownership

| File or directory | Responsibility |
| --- | --- |
| `base/_colors.scss` | Fixed primitive palette |
| `base/_themes.scss` | Complete semantic light/dark role maps |
| `base/_variables.scss` | Spacing, type, control sizes, radii, shadows and breakpoints |
| `base/_functions.scss` | Validated token access; no emitted selectors |
| `base/_mixins.scss` | Shared control behavior and responsive helpers |
| `base/_index.scss` | Public, non-emitting consumer API; hides implementation helpers |
| `global/_reset.scss` | Browser normalization only |
| `global/_typography.scss` | Global typography only |
| `global/_root.scss` | CSS property export and explicit theme scopes |
| `global/_token-export.scss` | Palette and legacy scale export helpers |
| `components/` | Reusable visual components |
| `layouts/`, `pages/` | Composition and page-specific placement |

Group styles by ownership, not by CSS property type. Dispatch-specific files live
under `pages/dispatch/`, including the rig illustration (`_rig.scss`) and its
animation states, keyframes and reduced-motion behavior (`_rig-motion.scss`).
Keep these separate files beside each other; there is no global animations folder
for a single page's behavior. `components/` contains shared UI, not page fragments.

`main.scss` loads the `global`, `layouts`, `components` and `pages` indexes. Complex
pages have their own `_index.scss`; simple pages remain one partial. `base/` is a
non-emitting library imported explicitly by consumers, not another global CSS layer.
The SCSS reachability test detects files that are not connected to `main.scss`.

UI consumers import only `@use '../base' as ui` (adjust the relative depth), not
private functions, variables or mixin files. `base` exposes validated `pg`, `fs`,
`theme`, `clr`, `size`, `radius`, `shadow`, `breakpoint` and shared control mixins.
Only `global/`, which exports the CSS contract, may inspect token maps directly.
The style tests enforce this dependency direction and prove that importing the
public API alone emits no CSS.

Map HTML popup content and details cards belong to `pages/fleet-map/`; the reusable
fuel recalculation button belongs to `components/fuel/_recalculate.scss`.
Related fuel controls live under `components/fuel/` (`plan-editor`, `reading`,
`recalculate`); driver hours, duty, arrival and stop-hour facts live under
`components/driver-status/`. Each folder has a single `_index.scss` imported by
the components index. Keep unrelated small controls as individual partials.
The unused pre-deck.gl truck marker/popup styles have been removed. Do not maintain
duplicate DOM implementations for renderer-owned markers.

## Choosing a token

Use `ui.theme(text)` for readable text, `ui.theme(surface)` for a panel, and
`ui.theme(on-accent)` only for text on a filled action. A border token is not a
text token. Fixed palette colors accessed with `clr()` are reserved for intentional
graphics and translucent effects, not theme-dependent body text.
`theme(focus, .12)` supports validated transparency without bypassing the theme.

Use `pg(sm)` for spacing, `fs(body)` for type, `size(control)` for control geometry,
`radius(sm)` for corners and `shadow(popup)` for shared elevation. These return CSS
properties and can be overridden at runtime. Numeric `pg()` is no longer supported.
Numeric `fs()` remains a legacy modular font scale, not a pixel count.
UI partials now use named font tokens exclusively; the legacy function is retained
only for compatibility. The migration rounds to the nearest named size (less than
1.4px difference at the default root). Shared 44px minimum touch heights use
`size(control-touch)` so they scale with the root font size.

The default spacing choices are `xs` (4px), `sm` (8px), `md` (12px),
`lg` (16px), `xl` (20px), `xxl` (24px), and `section` (32px) at a 16px root.
Only `hair` and `micro` supplement this scale for small details. Intermediate
spacing tokens were consolidated into the standard scale; their consumers move
by at most 2px at the default root size. Do not restore aliases for each pixel.

Use `ui.breakpoint(md)` or `ui.breakpoint(md, max)` in media queries. `md`
starts at 800px; its max edge is 799px. `map-mobile` intentionally starts at 768px.
Named content thresholds preserve existing card, route and paper layouts.

Do not create a token for every unique number. Pixel borders, SVG geometry and
viewport breakpoints are deliberate exceptions. Component-specific layout limits
may remain local until they are reused. Media queries require compile-time values,
not runtime CSS properties.

## Themes and checks

Set `data-theme="light"` or `data-theme="dark"` on the root element. The default
is light. Dark roles merge explicit overrides with stable shared roles; the test
suite checks that both themes expose the same contract. Provider-rendered maps and
GPU assets are separate from the SCSS theme and need their own visual validation.
Semantic maps derive their values from the primitive palette through the private,
validated `colors.value(group, shade)` helper. Literal colors belong in one palette,
not copied into light/dark maps. Changing a theme means changing semantic role
assignments, not editing page selectors.

Run `npm run styles:build` and `npm test` in `Client`. Token tests reject unknown
names and raw color literals in UI styles. Browser checks must also cover contrast,
focus, disabled states, 44px mobile targets and increased root font size. Passing
compilation alone is not proof that every page is visually correct.
The token suite also enforces a 4.5:1 contrast ratio for normal text roles on
surface, muted surface and canvas, and for text on action buttons, in both themes.
This is a token-contract check, not a substitute for testing composited backgrounds.

Before this stabilization pass, authenticated light-theme checks covered Dispatch Cards, Table, Papers and its
popup, mobile/desktop Fleet Map details, Users, Add User and Settings. Users and
Settings were inspected at the visible browser's 543px width without submitting
forms. Full dark-theme page inspection and the remaining dialogs are still pending.
Do not treat those earlier checks as validation of the current changes; current
verification and remaining runtime checks are recorded in `../archive/2026-09/stabilization-work.md`.

## Audit follow-up

The audit found and corrected mixed foreground/background roles, fixed dark text
on dark surfaces, duplicated fuel-action styling, incomplete theme role exports
and a clipped mobile summary container. Legacy numeric font scales and bespoke dispatch
paper/map geometry remain intentional review areas; no blanket conversion of their
measurements has been performed. Full authenticated page-by-page visual approval
is still required before treating the dark theme as production-ready.

The HOS component owns its clocks and responsive dial/type scaling in
`components/driver-status/_hours.scss`. Consumers configure `--hos-gap`,
`--hos-dial-size`, `--hos-wrap`, `--hos-clock-min-width`, `--hos-value-font-size`,
`--hos-label-font-size`, `--hos-label-line-height` and `--hos-ring-width` on their
own wrapper. Defaults use the shared `hos-dial` and `hos-dial-compact` size tokens.
The private `--_hos-dial-size` resolves the actual diameter once for both geometry
and value text. Page styles may place the outer `.driver-hours-panel`, but must
not target `.driver-hours` or its internal selectors. Mobile rules preserve
consumer-supplied dimensions rather than replacing them with fixed pixels.
Arrival estimates expose `--arrival-font-size` and `--arrival-detail-font-size`
for composition; Dispatch uses body-sized estimates without changing map popup
typography. Dispatch load cards use vertical stop timelines above a compact
remaining-mileage and fuel-stop footer. Loaded, empty and total mileage, financial
values, supporting stop details and cycle forecasts open in the shared native load
modal. Its width uses `size(content-load-dialog)`; stop cards share a responsive
grid, and native disclosures hold supplemental fields without expanding the
underlying Dispatch board. Financial values remain server-provided.

`Shared/PageHeader` owns the primary page heading and optional description/actions.
Primary headings use the main layout's content edge. Dispatch keeps its heading
and toolbar outside the view-specific body frame so Cards, Table and Papers share
the same top alignment. Page-specific centered maximum
widths must not shift the title. Settings keeps its readable form width with
`size(content-form)` below the common header. Pages own the gap around the header;
the header does not add a second outer margin.

Fleet Map page styles are composed by `pages/fleet-map/_index.scss` from the ordered
partials under `pages/fleet-map/`: layout, toolbar, truck information, date,
route information and details. Keep responsive rules with their owning partial.
The details partial owns the selected-info overlay within the map stage. It does
not reserve document-flow height. The floating fuel editor uses the same map
stage, with independently scrolling timeline/details and persistent save controls.
Dispatch Papers column, stack, tabs and entry each have one base definition;
do not append a second base block to override the first.
Papers uses plain status columns and separated cards. Decorative folded corners,
rotated sheets, negative overlap margins and folder pseudo-elements were removed.
The selected reader opens in `Shared/Dispatch/DispatchLoadDialog/DispatchLoadDialog.razor` after explicit selection,
with native modal focus, Escape and focus restoration. Its styles belong to
`components/_dispatch-load-dialog.scss`; document-stop business presentation reuses
the Dispatch stop and cycle components. Table and Papers share the same phase resolver as Cards
while retaining the load's underlying operational status separately.
