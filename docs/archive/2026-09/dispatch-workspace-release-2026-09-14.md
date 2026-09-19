# Dispatch workspace release, September 14, 2026

## Scope

This release continues the [workspace implementation][implementation]. It adds
the full-page editor for existing commercial loads, native activity and document
storage, explicit transfer summaries and protection for simultaneous editors.
TorqueAI remains an updating source behind the adapter, not the native domain.

- Stale saves return a conflict and retain the browser draft. Workspace/source
  revisions, serializable transactions, unique receipts and audit revisions
  protect separate server instances, not only one process.
- Non-overridden commercial and matched-stop content follows source updates.
  Local itinerary ownership protects order, assignments, location and schedule;
  conflicting structural imports need review instead of overwriting edits.
- Native Trailer drop and Trailer hook display independent confirmations and
  outgoing/incoming resources. Unknown actual times remain unknown. Identical
  locations or Pick Up labels are not proof of a transfer.
- Load projections use the same native visit IDs/order as the workspace. Actuals
  enrich disconnected responses without mutating source data or save baselines.
- Recorded stops use compact read-only facts. Editable future stops retain
  appointment windows, references and instructions. Disclosure controls expose
  valid expanded states and avoid repeating the completed status.

## Verification

The pre-deployment `bash test.sh all` passed with no failures or skips:

- Server: 1,879 tests.
- Client C#: 940 tests.
- JavaScript, styles and architecture: 538 tests.

The log is `artifacts/managed/scratch-wd51VX/full-suite.log`. The new three-case
native visit regression initially exposed duplicate external IDs in its fixture;
the fixture was corrected without weakening assertions before this full pass.
CSharpier checked 1,204 files; the fixture-only follow-up was formatted again.

The full Release gate then passed on that source, including strict solution
build with zero warnings/errors, all tests, typed JavaScript, Prettier, SCSS,
270 published asset hashes and seven JavaScript dependency graphs. Log:
`artifacts/managed/scratch-CgQqFH/release-gate.log`.

The staged artifact is `artifacts/managed/release-qfsatB/publish/wwwroot`.
The generic offline UI report is
`artifacts/managed/browser-ui-tsyldc/report.json`: 52 page checks across 12
viewport/theme/font-scale cases, zero failures, browser errors or unexpected
requests. EF reported no pending model changes after the final build.

The dedicated workspace smoke also passed all eight cases against that artifact:
`artifacts/managed/browser-ui-2ftmi8/report.json`, with zero failures, browser
errors or unexpected requests. It includes narrow layouts, transfer summaries,
draft/conflict behavior and fixture-only note/document interactions.

Two system-Chrome UI attempts and one bundled-browser attempt were incomplete:
one field wait, one browser termination and one screenshot timeout. No reported
DOM assertion, browser-console or unexpected-network failure preceded them.
The macOS crash report identified the Node-owned Chrome process terminating with
`EXC_GUARD` in native accessibility notification calls. The UI runner now starts
a fresh browser process per responsive scenario, retaining every page, assertion
and accessibility check. No timeouts, retries or browser features were relaxed.

## Recovery preparation

A fresh custom-format PostgreSQL backup was created before migration. The dump
is private, outside the repository, with mode `0600` in a `0700` directory:

`/Users/antonarlazarov/Developer/pulsartms-workspace-backup.V9q3un`

- File: `before-workspace.dump`.
- Size: 225,216,184 bytes; archive listing: 362 entries.
- SHA-256:
  `c1d54261f671237ca1f3b350123fd6d64e13d3647ee0e4136c7da59a673c6c5c`.

The reviewed SQL for `20260914153020_AddDispatchWorkspace` is additive: one
appointment time-zone column, five tables and six indexes. Its SHA-256 is
`b960057f30a55ae1eca899fee4f9afc239f455ef62f555cfba773304cde2ff7f`.
The migration runner verifies those exact bytes, target database, starting
history and preserved load/stop counts within one guarded transaction.

## Production migration

The guarded transaction committed on project `amftms`, Neon endpoint
`ep-blue-cherry-aefp3o1q`, database `neondb`. History advanced from 36 records
ending at `20260914123323_OptionalTransferEventTimes` to 37 ending at
`20260914153020_AddDispatchWorkspace`. All 385 loads and 938 source stops were
preserved. Post-commit inspection confirmed five new tables, the nonnullable
100-character time-zone column and zero invalid/unready indexes. The new tables
have 11 indexes in total, including primary keys.

The old local API and Client were stopped before rollout. The new editor must
stay unavailable until the new API is ready and the old importer has drained.
API and Client deployment evidence follows below.

The first Cloud Build, `b57ebdea-0d79-40cf-b175-d09d1d456d30`, did not deploy.
Its strict build and all 1,879 server tests passed; two existing Client tests
timed out: the 12-card batch refresh and future-selection polling scenarios.
The failed run was cancelled after the test step finished. A complete local
rerun still passed 1,879/940/538 (`scratch-6IBGP6/full-suite.log`). The build
worker was increased to `E2_HIGHCPU_8` without changing Cloud Run resources,
test assertions, timeout thresholds or selected tests.

The replacement build `df8d8071-5460-4ca7-86f5-ccb71b883008` passed the same
unfiltered 940 Client and 1,879 Server tests in 17 and 47 seconds respectively,
with a zero-warning/error strict build. The 538 Node checks and published
artifact checks passed before the API image build.

## API deployment

- Cloud Build: `df8d8071-5460-4ca7-86f5-ccb71b883008`, `SUCCESS`.
- Image digest:
  `sha256:f3d3efebf7bc7115abef7ad329c45f4489d30767fedf74821ee090a7e04a138d`.
- Revision: `amftms-api-00115-cm9`.
- Ready and serving 100% of traffic at `2026-09-14T18:35:01Z`.
- Prior revision `amftms-api-00114-j98` logged application shutdown at
  `2026-09-14T18:35:02.074342Z`.
- Public `/api/health/live`: HTTP 200, `Healthy`.
- New-revision error-severity log query returned no entries at acceptance.

Traffic remained explicitly pinned to revision 114 after the image deployment
command. Revision 115's digest was checked and traffic was then explicitly moved
to that exact revision before starting Client publication. No old tag was added
and no Cloud Run scaling or credential setting was changed.

The new synchronization worker acquired the lease after cutover. Its dispatch
import succeeded at `2026-09-14T18:37:03.645Z`; the subsequent checkpoint was
`18:38:00.187291Z`, with zero failures for the inspected non-truck jobs. The
local API was restarted with synchronization disabled after the old production
importer stopped, so it does not compete with the production worker.

## Client deployment and live acceptance

The first Client publication passed the full gate, including 1,879 Server,
940 Client and 538 Node tests, the strict build and all 52 offline UI checks.
Its log is `artifacts/managed/scratch-hlNwsH/deploy-client.log`; the published
artifact is `artifacts/managed/release-Qcycby/publish/wwwroot`. The UI report is
`artifacts/managed/browser-ui-1JVPZ0/report.json`.

Firebase live version `2f29c9d219ea98a5` was released at
`2026-09-14T18:39:24.061Z`. The public index, versioned stylesheet, Client WASM
and Blazor bootstrap returned HTTP 200 and matched the staged bytes. The index
and stylesheet retained revalidation; fingerprinted framework files remained
immutable.

Authenticated, read-only acceptance through localhost against the shared data
confirmed AMF1377's independent completed Drop and Hook, with 11005 handing off
to 54777 and trailer 9P1175 continuing to delivery. The board also showed
AMF1383 assigned to 11005 after the transfer. No business records were edited.
This is not an authenticated end-to-end test through the public production UI.

That acceptance found two Client navigation defects: completed-stop links used
a literal Razor expression, and the workspace map link used an outdated source
header truck. The follow-up uses exact visit links and the current saved native
assignment, independent of the selected historical editor row. Unresolved native
assignments do not fall back to the old source truck. Five Client regressions
cover these cases. Further review added seven transfer-edge cases: a confirmed
release excludes its outgoing leg even if an earlier pickup has no actual time.
Planned releases do not exclude the leg or fabricate completion.

## Follow-up interface revision

Before final publication the user requested a simpler workspace and map card:

- Stop selection changes a dedicated editor below the complete itinerary,
  without inserting content between rows or collapsing repeated selections.
  Browser checks compare every row's document coordinates and dimensions.
- Overview places broker contacts, load-wide Notes and immediate document
  uploads beside the itinerary. Assignments replaces the Execution tab label.
- New notes use one text field, no stop/driver/type selectors, and no permanent
  Refresh action. Existing issue/history records remain available.
- Choosing or dropping files uploads sequentially. Uncertainty keeps the same
  payload/key; remaining files pause. Discarding a queue preserves saved files.
  Browser read failures also expose Retry/Discard instead of trapping the queue.
- Fleet telemetry/HOS now precedes load information, with both panels visible
  in the existing bounded scroll container. Truck disclosure state is removed;
  Next load stop disclosure remains separate. Weather icons use condition
  colors.

`bash test.sh all` passed 1,879 Server, 968 Client and 539 Node tests with no
failures or skips: `artifacts/managed/scratch-cc7r1A/full-suite.log`.
An earlier attempt was cancelled when a synchronous file-selection test blocked
on its own held HTTP response. The regression now uses an explicit pending task
and request signal, retaining cancellation and wrong-load assertions. A route
input test now awaits input and its rendered button state. No production timeout
or assertion was relaxed.

The full strict gate passed those same 3,386 tests, zero build warnings/errors,
Prettier, typed JavaScript, SCSS and all asset/dependency checks. It also passed
52 generic offline page checks across 12 cases, including stable itinerary
coordinates. Log: `artifacts/managed/scratch-sjkqac/release-gate.log`.
UI report: `artifacts/managed/browser-ui-RmoFoN/report.json`.
Artifact: `artifacts/managed/release-AvMaHO/publish/wwwroot`.
The subsequent final package also moves broker contacts and load pricing out of
Overview into Broker & billing. The same component instances and load draft
survive section changes without another request. Notes, documents and load
instructions remain beside the route. Two remaining journal hints now say Notes.

A short-phone fuel-editor check found that the two-row action footer consumed
working space. The phone footer now uses three wrapping action columns with
unchanged labels and touch targets. A compiled-style regression covers this.
Short-screen probes retain two complete operational rows after scrolling the
nonsticky introduction when necessary, and verify every row is reachable and
uncovered before real pointer/touch reorder checks. Working-pane height is
checked against its actual header/footer boundaries, not an arbitrary pixel
floor. Desktop layout remains unchanged.

One repeated Client run exposed a test render race: a held preview request can
be observed before the inspector render. Each staged appointment assertion now
waits for rendering while its HTTP response remains held. Timing-slot, value and
duplicate-row assertions are unchanged; no production behavior was changed.

The final strict gate passed 1,879 Server, 969 Client and 540 Node tests,
zero warnings/errors, format, typed JavaScript, SCSS and artifact checks.
Log: `artifacts/managed/scratch-mntvEB/release-gate.log`.
Affected Dispatch/styles checks also passed in `scratch-mntvEB/affected.log`.
Artifact: `artifacts/managed/release-z5e91C/publish/wwwroot`.

Offline browser acceptance against that final artifact passed:

- Generic UI: 52 page visits across 12 cases; `browser-ui-4s6h9R/report.json`.
- Dedicated workspace: 8 cases; `browser-ui-sL1l2v/report.json`.
- Fleet: 12 cases; `browser-hours-forecast-dHxeYO/report.json`.
- Fuel editor: 8 cases; `browser-fuel-editor-OruTe3/report.json`.
- Route editor: 8 cases; `browser-route-editor-0EbRMU/report.json`.
- Generic UI with broker-tab captures: 52 visits across 12 cases;
  `browser-ui-wdf2st/report.json`.

All listed reports are under `artifacts/managed`, with no browser errors or
unexpected requests. Browser fixtures now explicitly provide personal units and
weather through their actual endpoints. Text checks normalize nonbreaking
spaces without changing values. ETA checks distinguish arrival status from the
separate cycle warning. The long-address fixture actually overflows at all
tested widths and still verifies ellipsis without column growth.

An additional footer-only probe passed eight 320/390px, 100%/200%, light/dark
cases against published CSS and the actual footer markup:
`artifacts/managed/scratch-PMXyjN/report.json`. It preserves all three labels,
touch targets and text sizes; this is not the full fuel workflow at 200%.

Authenticated read-only localhost acceptance confirmed the new Notes/drop-zone
layout, the separate stop editor and AMF1377's map link to current truck 54777.
API revision 115 remained at 100% traffic, with a healthy public liveness
endpoint and no error-severity entries in the final pre-publication
30-minute query.
No additional server migration or API image is needed for these Client changes.

## Final Client publication

Firebase live version `930742ac2f3a3081` was released at
`2026-09-14T19:49:45.307Z`, using the exact verified `release-z5e91C` artifact.
The public index, versioned stylesheet, Client WASM and Blazor bootstrap
returned HTTP 200 and matched their staged SHA-256 hashes. Public liveness
returned HTTP 200, `Healthy`; API rewrites and cache policies were unchanged.

The active-tab screenshot concern was an in-progress color transition and
normal pointer hover, not a selection-state failure. A bounded desktop browser
case verified exactly one current tab, recorded immediate/settled styles and
captured the final selected state: `browser-ui-otBe4J`. The browser helper and
existing Client regression now assert current-tab identity. Follow-up
Dispatch/styles checks passed 592 Client and 761 Server tests plus their Node
groups; final Prettier passed. Logs are in
`artifacts/managed/scratch-k3U4GR`. These supplement the full strict gate above;
they are not reported as another full-suite run.

Local Client was rebuilt with zero warnings/errors and restarted on port 5067.
The local API keeps migration application and synchronization disabled.

## Limits

No isolated PostgreSQL fixture was available: PostgreSQL integration tests and a
full backup restore were not run. SQLite fixtures do not establish PostgreSQL
locking behavior under real multi-instance load. Production schema inspections
are operational checks, not a substitute test database.

Offline browser fixtures do not verify real map/GPU rendering or production
performance. No production latency or memory improvement is claimed. Native
commercial-load creation, payroll, invoicing and full structural import
reconciliation remain separate workflows. Documents have signature validation,
not malware scanning. No production notes or documents are created as tests.

[implementation]: dispatch-workspace-2026-09-14.md
