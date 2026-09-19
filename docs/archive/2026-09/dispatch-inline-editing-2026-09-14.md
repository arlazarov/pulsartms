# Dispatch inline editing — 2026-09-14

## Changes

- Ordinary stops expose status and resource selectors directly, including completed
  stops. Changes use one explicit Save without a reason or fabricated actual time.
- Resource field masks preserve unchanged truck/trailer/driver/co-driver values.
  Scope supports current assignment, all assignments, onward and explicit ranges
  covering complete existing legs. Partial-leg splitting is not implemented.
  Confirmed trailer custody and shared-load protections remain enforced.
- Scope resolution and validation remain in Application. Client only previews
  affected stops; the server checks scope, resources, source/workspace revisions
  and retry identity before committing. Completion affects the selected stop only.
- The stop list is bounded to 22rem / 45vh with internal scrolling. Its header
  and selected-stop editor remain outside. Repeated-visit counters and arrow
  buttons are removed from this workspace only. The reorder handle supports
  dragging, keyboard Up/Down and handle-then-destination taps.
- Semibold labels and shared icons distinguish direct fields from supporting text.

## Verification

- Strict Release Client publish and SCSS compilation succeeded.
- Dispatch/styles group succeeded: 610 Client and 787 server tests plus its
  JavaScript/architecture groups before the final source co-driver guard.
- The final full .NET attempt passed 1,906 server tests and 986 of 987 Client
  tests. DispatchBatchRefreshTests.TwelveCardsUseOneSummaryReadAndPageStatusWhileUnchangedStatusDoesNotRenderAgain
  intermittently fails waiting for planning snapshots. An earlier full run passed
  987 Client, 1,905 server and 545 JavaScript tests; this does not make the final
  run a full pass. The intermittent failure is not claimed fixed.
- Eight staged browser workspace scenarios passed at 1440, 390 and 320px,
  light/dark, with normal and enlarged text. Report:
  artifacts/managed/browser-ui-Tajgrw/report.json.
- A separate isolated-data localhost check verified visible direct controls,
  font weight 600, a 352px scrollable list, status saving, and no document
  horizontal overflow at 390px. These checks do not prove live integration.

## Delivery boundaries

The preview runs at localhost:5079 with synthetic data and in-memory demo writes.
An existing tab with an unsaved draft was preserved; a fresh preview tab was opened.
No deployment, production writes, migrations or PostgreSQL checks were performed.
The previously added AddBrokerProfiles and AddStopCorrections migrations still
require deployment-time application. No performance improvement is claimed.

## Follow-up: direct transfers and contained workspace

- Drop/Hook stops expose resource selectors directly. Truck/driver corrections
  affect the selected leg; supported trailer corrections update both connected
  legs and custody together, retaining actual confirmations and timestamps.
  Shared loads and wider connected transfer chains remain protected.
- Desktop now fits the app viewport: the stop list, selected-stop editor and
  right information column scroll independently. Narrow screens stack columns
  inside a workspace scroll region. Headers and the Add stop choices remain
  accessible without document scrolling. The list is capped to 35% of its pane
  and the named dispatch-stop-list size.
- Load identity and summary share a wrapping row with semibold values and
  separators. Routine source/import text and duplicate unsaved-edit notices
  were removed; save state, conflict feedback and edit guards remain.
- Dragging shows a before/after insertion line without mutating order until drop.
  Native drag-event browser checks include DataTransfer; synthetic drag events
  without it are not valid Blazor drag payloads.

### Latest checks

- Strict Release Client publish and style build succeeded.
- Dispatch/styles runner: 793 server tests passed; 611 of 612 Client tests
  passed. The same existing DispatchBatchRefreshTests snapshot-wait failure
  recurred. This is not a complete category or full-suite pass.
- The separate style suite passed after updating its expected viewport-relative
  cap. Earlier full-suite evidence above is historical, not a final full pass.
- All eight final staged browser scenarios passed, including drag feedback,
  keyboard/tap reorder, save/discard, transfer selection, notes and documents,
  light/dark and enlarged text. Report:
  artifacts/managed/browser-ui-n5oNfF/report.json.
- Isolated preview checks at 1600px and 390px confirmed document height equals
  the 1000px viewport and selected-stop content remains independently scrollable.
- No server deployment, live-data writes, migration or PostgreSQL test was run.

### Header and legacy action cleanup

Removed Copy load link / Link copied, Managed in PulsR and the standalone Truck
starting stop editor from the full-page workspace. Existing planning overrides,
shared legacy editors elsewhere and server calculations are unchanged.
Strict Client publish succeeded. All eight browser scenarios passed in
artifacts/managed/browser-ui-SuN9io/report.json. The Dispatch group passed 793
server tests and 611/612 Client tests; the existing batch-refresh snapshot timeout
recurred. The affected page/component regression checks passed. No full-suite
pass or deployment is claimed.

### Visual hierarchy refinement

Added operation icons and semantic tones, compact completed badges, a selected
stop/facility heading, grouped direct assignments, clearer field headings and
compact document upload. The existing-stop disabled type field is omitted;
its operational edit action and new-stop type selection remain available.
Selection reveals the row using list-only scrolling, without document scrolling
or changes to route order. Resource selects are keyed by their option collections
so loading the real choices does not reset the displayed selection to the first
option. No assignment is saved by this presentation fix.

Strict Client publish, style/JavaScript builds and JavaScript type checking passed.
Dispatch/styles: 612 Client tests, 793 server tests and included JavaScript and
architecture groups passed. Nine staged browser scenarios passed in
artifacts/managed/browser-ui-MsfoYo/report.json, including the dedicated native
resource-selection regression and narrow/enlarged-text layouts. This is an
affected-category pass, not a new full-suite run. Localhost remains a synthetic
preview; no production deployment or migration was performed.

### Selected concept 03 implementation

Replaced the vertical itinerary with a horizontal stop strip above the editor,
route/mileage and notes/documents panes. Appointment and reference cards appear
first; instructions remain with references, with location/contact and cargo below.
The direct status/resource controls, native transfer workflow, draft guards,
server mileage and document upload behavior remain available. The sidebar
navigation is unchanged; this implements the selected workspace content layout,
not a global navigation redesign.

Selection scrolls only the strip. Drag insertion markers now indicate the
left/right insertion edge, with both horizontal and legacy vertical keyboard
arrows supported. Enlarged tiles are bounded by the strip width.
Load refresh retains mileage, notes, documents and transfer component identities.
The read-only map consumes stop coordinates, retains its camera on selection and
disposes markers/listeners. It does not invent road geometry or distances; the
road route remains in Fleet Map. Missing configuration/coordinates are explicit.

Final verification:

- Strict Release Client publish, SCSS build, JavaScript build/type check and
  targeted JavaScript/SCSS formatting check passed.
- Full runner passed: 991 Client, 1,911 server and 548 JavaScript tests, including
  architecture. Log: /tmp/pulsr-stop-workspace3-gate.log.
- All nine staged synthetic browser scenarios passed, including light/dark,
  320/390/1440 widths, enlarged text and retained native resource selections.
  Report: artifacts/managed/browser-ui-WnVwIZ/report.json.
- Earlier iterations exposed the slot-remount regression, oversized mobile
  tiles and test fixture/configuration issues; these were corrected. Earlier
  full runs also saw the previously observed batch-refresh timeout and a
  completed-document loading timeout. The final pass is not a claim that those
  timing-sensitive tests can never recur.
- No PostgreSQL execution fixture, live Google Maps/provider verification,
  migration, production deployment or production performance measurement ran.
  Localhost 5079 remains the synthetic preview.

### Confirmed table reference

The user subsequently confirmed the attached Dispatch Table image as the target,
superseding the horizontal-tile interpretation. The workspace now places a compact
stop table over a flat two-column editor, with Notes, Documents and route/mileage
in the right scroll pane. Resource names and ETA are visible in table rows.
Insertion indicators and selection reveal use the vertical axis again.
The existing app navigation remains unchanged.

On narrow/enlarged-text layouts the bounded workspace scrolls its stacked panes;
the selected editor retains usable height instead of overlapping Notes. An active
transfer uses the editing pane while retaining the ordinary form in the DOM.
The completed-document regression now observes and interacts with the owning
Documents component, avoiding stale parent bUnit event bindings.

Verification: strict Release publish, JavaScript build/type check, SCSS build and
format checks passed. Dispatch/styles runner passed 614 Client and 793 server
tests plus 136 style, 12 Dispatch JavaScript and 47 architecture JavaScript tests.
The final SCSS adjustment additionally passed all 136 style tests. All nine
synthetic browser scenarios passed in
artifacts/managed/browser-ui-zFoPDM/report.json. This is an affected-category
result, not another full-suite run. No live provider validation, PostgreSQL
fixture, migration or deployment was performed.

### More room for the selected-stop editor

Reduced the desktop stop-list cap to 24% and the shared compact 12rem token,
with a bounded mobile list. The selected-stop caption and facility share a
compact heading. The editor retains the remaining height and independent scroll;
the right supporting pane remains independently scrollable.

Verification: strict Release Client publish, SCSS build and formatting passed.
The styles runner passed 2 Client and 55 server checks, 136 style JavaScript
tests and 47 architecture JavaScript tests. All nine synthetic browser scenarios
passed, including a desktop regression requiring the editor height to exceed
1.5 times the stop-list height. Report:
artifacts/managed/browser-ui-2re2sK/report.json. The desktop screenshot was
visually inspected. This was an affected-category run, not a full-suite run.
Published to the synthetic localhost 5079 preview only; no migration, live
provider verification or production deployment was performed.

### Compact table header and column priorities

Merged the stop count and Add stop action into the table header. The add action
uses a shared icon-sized button with its accessible name, tooltip and existing
selected-stop anchor behavior. Narrow layouts retain both controls. Expanded the
stop-identity allocation and reduced ETA width, preserving wrapping full dates
and resource names without changing forecasts or requests.

Strict Release publish, SCSS build and formatting passed. The Dispatch/styles
runner passed 614 Client and 793 server checks, plus 137 style, 12 Dispatch and
47 architecture JavaScript checks. All nine synthetic browser cases passed in
artifacts/managed/browser-ui-eBArl9/report.json, including the reachable header
action and desktop identity-to-ETA width regression. Desktop and enlarged-text
mobile screenshots were inspected. This is affected-category evidence, not a
full-suite or production validation. Updated the synthetic localhost 5079
preview only; no deployment or migration was performed.

### Stop-list breathing room

Increased the compact list cap from 12rem/24% to 15rem/30%, with a 32dvh
mobile bound. The compact header and independent editor scroll remain intact.
Strict Release publish, SCSS build and formatting passed. The styles runner
passed 2 Client, 55 server, 137 style JavaScript and 47 architecture JavaScript
checks. All nine synthetic browser cases passed in
artifacts/managed/browser-ui-Hn0DuB/report.json; the desktop screenshot was
visually inspected. This was an affected-category run. Updated localhost 5079
only, with no deployment, migration or live-provider validation.

### Header navigation and directly editable operation

Moved Broker & billing, Overview and History beside Back to Dispatch in the
existing top row. Section contents remain mounted and retain their drafts.
The workspace enables the shared operation editor's inline mode: Type and
After this stop are immediately available, with Save/Cancel appearing for a
changed draft. Imported-operation reset remains explicit. Other consumers keep
the existing disclosure behavior. Operation drafts guard competing edits and
navigation; requests retain the opened operation revision and completion identity.
Idle fields follow source refreshes, while active drafts retain their values.
Pickup/Delivery labels map to the existing command values without server changes.

Strict Release Client publish, SCSS build and formatting passed. Dispatch/styles
checks passed: 617 Client, 793 server, 137 style JavaScript, 12 Dispatch JavaScript
and 47 architecture JavaScript tests. All nine synthetic browser cases passed in
artifacts/managed/browser-ui-ySZiJk/report.json, including inline selection,
cancel-without-write, competing-action locking and retained section navigation.
The desktop screenshot was visually inspected. This is an affected-category
run, not a full-suite or live-operation validation. Updated localhost 5079 only;
no production deployment, database execution or migration was performed.

### Completion toggle and operation-state clarity

Ordinary stop corrections now use a pressed-state Completed button instead of
a status dropdown. The existing explicit Save/Cancel and retry identity remain;
transfer confirmations are not undone by this control. Inline operations omit
the state selector for single-state actions and label ambiguous choices Trailer
after stop, preserving partial-delivery state choice.

Read-only diagnosis: the workspace reader locks recorded transfer boundaries,
and workspace validation rejects changes to their editable hash. The Client then
renders DispatchStopSummary instead of address fields. No transfer address
correction was implemented and no backend lock was bypassed.

Dispatch/styles passed 617 Client and 793 server checks plus 137 style,
12 Dispatch and 47 architecture JavaScript checks. A browser regression exposed
selected-hover styling and immediate transition sampling; both were corrected.
The final style run passed 2 Client, 55 server, 137 style and 47 architecture
JavaScript checks. Strict Release publish passed, and all nine synthetic browser
cases passed in artifacts/managed/browser-ui-xpYwb7/report.json. This is
affected-category evidence only. Updated localhost 5079; no production
deployment, migration or live transfer mutation was performed.

### Mobile panes and header-owned stop actions

Mobile Overview now exposes Stops, Details, Notes & files and Route. One pane
uses the available workspace height; selecting a stop opens Details. Components
remain mounted through pane changes, retaining drafts, notes and documents.
The selected-stop heading scrolls with its fields on narrow layouts to leave
usable editing space at enlarged text sizes. Desktop keeps its split workspace.

Operation and resource/status drafts now use the page header Save changes and
Discard, without duplicate local buttons. Standalone shared editors retain their
own actions. Revision guards, uncertain correction retry identity and competing
draft protection remain in place. New component cases exercise save and discard
for both child editors through the page header.

Strict Release publish and SCSS compilation passed. Dispatch/styles passed 619
Client, 793 server, 137 style JavaScript, 12 Dispatch JavaScript and 47 JavaScript
architecture checks. All nine synthetic browser cases passed in
artifacts/managed/browser-ui-q0op1b/report.json, including 320/390px, both themes,
200% root text sizing, retained drafts, header actions and pane-local scrolling.
Mobile screenshots were inspected. This is affected-category evidence, not a
full-suite or live-provider result. Localhost 5079 was updated; no deployment,
migration or PostgreSQL execution checks were performed. Transfer address
correction remains unimplemented pending clarification of linked-stop scope.

### Direct resource navigation and company directory

Added Trucks, Trailers and Drivers to main navigation, reusing the existing
Admin-only configuration pages. Settings links use exact matching to avoid
highlighting Settings alongside a selected resource. No permissions or resource
identities changed. Customers & brokers at `/customers` reuses the existing
Customer master through broker search and revision-checked profile writes.
Search is explicit with at least two characters and a 20-result limit. The page
supports company creation, contacts and shared billing/Quick Pay defaults, guards
unsaved navigation and retains drafts after failures. Existing saved load terms
are not rewritten. BrokerTerms moved to Shared/Customers with component-owned
styles; payment execution and driver Pay remain outside scope.

The generic Transfer yard display name now falls back to the stop's available
address. Real facility names and stored snapshots remain unchanged. No transfer
stop was deleted and no confirmed-transfer address guard was bypassed.

The strict Client Release publish and SCSS build passed. Dispatch, Identity and
Styles checks passed 690 Client and 923 server tests, plus 137 style, 7 identity,
12 Dispatch and 47 architecture JavaScript checks. Eleven synthetic browser
cases passed in artifacts/managed/browser-ui-D58Dv8/report.json. After matching
the resource page's Settings shortcut exactly, the focused directory/resource
selection cases passed in artifacts/managed/browser-ui-FMWMRk/report.json.
Desktop and phone directory screenshots were inspected. This is affected-category
evidence, not a full-suite or production performance claim.

Localhost 5079 was republished. Its isolated demonstration host gained in-memory
company and resource endpoints; the existing demo workspace was snapshotted and
restored across the host restart. No real company/resource data, production
deployment, migrations, live provider checks or PostgreSQL execution were involved.
