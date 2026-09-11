# Application design and fuel timeline — September 9, 2026

User review subsequently identified that this initial pass retained the old
Dispatch composition and did not reproduce the approved visual mockups. The
verification below applies to that initial restyling/functional pass, not approval
of the completed design. A structural layout follow-up is in progress.

## Scope

Implemented a compact application foundation in the existing SCSS
hierarchy: shared semantic themes, controls, navigation, cards, tables, forms,
Dispatch, Fleet Map, Users, Settings and account pages. No inline style system,
new UI framework or separate page palettes were introduced. Map price colors,
pickup/delivery roles and route geometry remain separate from interface selection
tokens. Illustrative mockups were not used as literal map or business data.

Dispatch Cards, Table and Papers expose server-provided rate, loaded RPM and total
RPM. Completed pickups remain visible. A Loads selector uses the existing
completed-dispatch endpoint; its bounded completed projection includes stop and
financial details without live HOS, ETA, routing or equipment requests. Current and
completed request generations remain isolated. No unsupported load-creation
actions were added.

The fuel editor uses one timeline with fixed pickup/delivery anchors and draggable
fuel visits. Mouse, touch and keyboard ordering update both visit sequence and
mandatory-leg placement. Selecting a visit highlights and centers its unchanged
price marker in the visible map area; changing quantity does not refocus it.
Mobile leaves an exposed map strip and scroll access to the editor controls.

Application returns purchase costs in normalized USD and per-visit purchase
limits. At 211.3 gal capacity with 34 gal on arrival, the limit is 177.3 gal:
the slider endpoint maps to exact Full tank, and one step back buys 170 gal.
Incoming limit values are discarded. Unresolved balances do not invent numeric
limits; stable draft keys retain the appropriate slider geometry during pending
responses. See the maintained [fuel rules](../../features/fuel-planning-rules.md).

There is one current truck fuel plan, no version history and no BVD transaction
matching. This pass introduces no migration or production deployment.

## Verification

- Final `bash test.sh all`: 1,277 Server, 517 Client C# and 302 Node tests passed
  (2,096 total), including both architecture suites and JavaScript architecture.
- JavaScript typecheck/build and SCSS compilation passed.
- Strict Release solution build passed with zero warnings/errors; both unfiltered
  Release test assemblies passed (1,277 Server and 517 Client).
- Client published to the fresh local directory
  `artifacts/design-verified.3455Hb/publish/wwwroot`. Artifact verification checked
  234 assets, their integrity/compressed variants, and seven JavaScript entry-point
  dependency graphs.
- Offline staged UI matrix: 44 page cases across desktop/mobile, light/dark and
  100%/200% root text scale, including additional Table/Papers/Completed interactions.
  No geometry failures, browser errors or unexpected requests remained.
  Report: `Client/test-results/ui-design-verified/report.json`.
- Staged fuel editor: four desktop/mobile theme cases passed, including trusted
  mouse/touch drags across anchors and fuel rows, keyboard order, native slider
  ArrowLeft/End behavior, server cost display, focus callbacks, save/cancel fixture
  semantics and contrast/bounds. Report:
  `Client/test-results/fuel-editor-design-verified/report.json`.
- Production GPU/popup fixture: 40 cases passed with no browser errors or
  unexpected requests. Report:
  `Client/test-results/stop-cards-design-final/report.json`.
- Representative desktop Table/Papers, fuel editor, dark Users and mobile Settings
  screenshots were visually inspected. Browser findings corrected before the
  final matrix included header alignment, filter height, mobile text-scale overflow
  and unnecessary Papers reader height.
- Local Release Client/API restarted on 5067/5086 with migrations, synchronization
  and Gmail background maintenance disabled. Client returned HTTP 200; anonymous
  `auth/me` correctly returned 401. The existing authenticated local Dispatch was
  read without saving data; current loads, completed pickups and financial values
  were visible.

## Intermediate failures and limitations

A bUnit event-handler race appeared in an early Release run. The test now performs
lookup and dispatch on the renderer dispatcher and explicitly holds/releases
pending previews. Its assertions were retained and repeated isolated Release runs
passed before the final complete runs.

One Debug full run reported 9,000 allocated bytes instead of the expected 1,280
for 40 dense-route matches. The unchanged allocation test subsequently passed two
isolated runs, two complete Server runs, the final Debug suite and Release suites.
Diagnostic runs consistently measured 1,280 bytes (one result point per match),
with no per-refined-point buffer path found. The one-time excess was not explained;
runtime/tiering noise is a hypothesis, not an established cause. Neither the
4,096-byte threshold nor production matching code was changed. Local evidence:
`artifacts/allocation-diagnose/allocation-full-isolated.trx`.

Browser fixtures intercept all requests, including simulated writes; they do not
save real fuel plans or validate live provider calculations. The authenticated
map lifecycle/heap soak, real PostgreSQL execution, production query plans,
performance and exhaustive live visual acceptance were not run. No approved
isolated PostgreSQL fixture was available; no local SQL server was started.
Existing migration application was disabled, and no new migration was created.
Blazor published without optional wasm-tools native optimization; that workload
was not installed as part of this UI change. Production remains unchanged.
