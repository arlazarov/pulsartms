# Full project audit — 2026-09-07

## Executive assessment

The project has a useful modular foundation. The main problem is no longer folder naming: several independently reasonable implementations disagree about ownership, identity, freshness, or invalidation. Those disagreements cause incorrect data, UI regressions, redundant work, and incomplete request accounting.

An indiscriminate rewrite is not justified. Stabilize the invariants below, consolidate the implementations that own them, and enforce those invariants with executable tests. Do not introduce microservices, another generic repository, or a universal state framework to solve these specific defects.

This is an audit, not a remediation release. No application source, configuration, database, credentials, or deployment was changed by this audit. This report is the only maintained file added.

## Scope and evidence

- Inventoried 697 maintained code, documentation, and script files after excluding 22 generated migration designer/snapshot files from the inventory. Build outputs, dependencies, published artifacts, and generated browser bundles were not treated as maintained source. Relevant configuration and artifact metadata were inspected separately.
- Reviewed Application/Infrastructure/Domain boundaries, API authorization, identity, dispatch synchronization, routing and provider accounting, deadheads and financial reads, fuel planning/import, Client services, Blazor state and polling, JS rendering/lifecycle, SCSS, tests, and release scripts.
- Read all 52 maintained SCSS files, including `base`, and inspected their import/token relationships.
- Used cross-cutting scans plus deep tracing of critical execution paths. This does **not** mean every passive DTO, generated migration, or static markup line received exhaustive semantic validation.
- Ran `bash test.sh all`: **361 server tests and 69 Client tests passed**. No skipped or failed tests were reported. This audit did not run a separate strict Client build or publish.
- Ran two isolated JS diagnostics for same-version route metadata and stop-layer updates. Measured page-heading geometry in a browser fixture using the actual compiled CSS. Read the existing visible Fleet Map state without changing business data.
- Release-script syntax checks passed. MSBuild content evaluation confirmed the credential-file packaging finding without reading credential values.
- Did not perform authenticated end-to-end coverage of every page, a long memory soak, real GPU profiling, PostgreSQL query plans, concurrency/load tests, cloud-scheduler inspection, provider billing reconciliation, migrations, or deployment.
- No live database queries or paid provider calls were used. The local application shares the existing database; no additional database was created.
- This workspace has no `.git` metadata. Change provenance, tracked-secret exposure, and a deployed-versus-working-tree diff could not be established.

Evidence terms below distinguish a reproduced local fixture, a confirmed source path with a stated precondition, and an operational risk requiring external verification. Priorities are release priorities, not claims that every scenario is occurring in production.

## Release-critical findings

### A01 — P1: Credential file propagates into build/publish artifacts

**Evidence:** [API.csproj](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/API/API.csproj:1). Web SDK content evaluation includes `gmail-credentials.json` with both output and publish copying enabled. Copies exist in local publish, API build, and test output directories.

**Impact:** A credential-bearing file can be distributed with an otherwise ordinary .NET artifact. This broadens its exposure unnecessarily.

**Boundary:** `.dockerignore` and `.gcloudignore` already exclude the file from cloud Docker build inputs. No public disclosure or compromised credential was established; credential contents were not read.

**Correction:** Explicitly exclude credential/token paths from MSBuild output and publish content. Keep runtime secrets outside distributable artifacts. Inspect existing artifact distribution before deciding whether rotation is necessary.

**Acceptance:** Publish-content and produced-artifact checks reject secret filenames; runtime configuration still resolves through the approved secret mechanism.

### A02 — P1: Conflicting stop/truck assignments can route an entire load for the wrong truck

**Evidence:** [RoutePlanningService.LoadAsync](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Routing/Services/RoutePlanningService.cs:328). Stop assignment validation runs only when the dispatch header has no truck. With header truck A and a stop assigned to B, the load is accepted for A; route construction consumes all stops. Stop truck assignments are also absent from `HashInputs` at line 365.

**Impact:** GPS, route, vehicle profile, and downstream fuel planning can be associated with the wrong assignment. A stop reassignment can leave the saved input signature unchanged.

**Correction:** Centralize assignment resolution and validate the whole assignment set. Reject ambiguous loads unless split-load routing is explicitly supported. Include relevant assignment identity in saved-plan signatures. Future-load `FuelHorizon` already has a stricter check that should not diverge from current-load planning.

**Acceptance:** Header A/stop B, no header/multiple trucks, and reassignment-after-save tests cannot silently produce or reuse a whole-load route for A. This is source-confirmed; production occurrence was not measured.

## Correctness, identity, and data ownership

### A03 — P2: Synchronization overwrites verified coordinates but preserves verification

**Evidence:** [DispatchMapper.UpdateStop](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Dispatch/Commands/SyncDispatche/DispatchMapper.cs:64). An unchanged source address preserves normalized address text and `AddressVerifiedAt`, but lines 71–72 always replace latitude/longitude with imported values, including null. `StopAddressService.VerifyAsync` skips an already-verified stop; its expiration policy is 29 days.

**Impact:** Consumers can receive degraded or missing coordinates while the record still claims verification. Route/deadhead signatures can change unnecessarily.

**Correction:** Preserve verified coordinates with the unchanged verified address, or explicitly separate imported and verified location fields. Address text, point, provenance, and verification lifetime must form one coherent value.

**Acceptance:** Extend the existing correction/sync regression to assert coordinates, not just text and timestamp. Unchanged-source sync preserves the verified point; changed-source sync invalidates the complete verified value.

### A04 — P2: Malformed successful provider responses roll back billable request accounting

**Evidence:** [TomTomRoutingProvider](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Infrastructure/Integrations/TomTom/TomTomRoutingProvider.cs:105). `{"routes":[{}]}` is valid JSON but throws `KeyNotFoundException` from the parser. The catch filter at line 110 excludes it. The transaction opened at line 73 then rolls back the request row inserted before the HTTP call.

**Impact:** A request that reached the provider is absent from the local quota ledger, and its failure cooldown is lost. Repeated structurally invalid responses can bypass the intended request-count protection.

**Correction:** Validate response shape into a handled provider failure. Persist a quota reservation independently before external I/O, then finalize success/failure without erasing an attempted request. Preserve cross-process quota serialization while shortening its transaction scope.

**Acceptance:** Missing fields, wrong JSON types, cancellation after dispatch, and process-interruption recovery all retain attempted-call accounting. A retry within the failure window makes no new HTTP request. Use a fake provider for these tests.

### A05 — P2: A failed request can be retried under another signed-in account

**Evidence:** [AuthHeaderHandler](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Services/AuthHeaderHandler.cs:43). Any changed access token is treated as a concurrent refresh; the original method/body is retried with that token at lines 71–73.

**Precondition:** Account A has an outstanding request; another tab signs into B using shared local storage before A's 401 is processed.

**Impact:** A's intended operation can execute with B's identity and permissions. Existing comparisons protect a change during one refresh branch, but do not distinguish a prior account change from same-account token rotation.

**Correction:** Bind requests and refresh operations to a stable authentication session generation/account identity. Cancel or reject replay across login/logout/account changes.

**Acceptance:** A delayed A response after login B never sends A's original operation under B. Same-session concurrent refresh still performs one refresh and retries normally.

### A06 — P2: Concurrent first role assignments can create duplicate role claims

**Evidence:** [UserRoleService.SetAsync](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Infrastructure/Identity/UserRoleService.cs:27). Two first assignments can both read no claim and insert one. Role-only updates do not serialize on a changed user row, and the claim index is not unique for this role invariant.

**Impact:** Later `SingleOrDefaultAsync` and `ToDictionaryAsync` role lookups can throw for the account or a user list containing it.

**Correction:** Enforce one application-role claim per identity in persistence, with an atomic or serialized assignment path and a deliberate duplicate-data migration strategy. Do not impose uniqueness on unrelated claim types that legitimately allow multiple values.

**Acceptance:** Concurrent first assignments leave exactly one valid role and working authorization/list reads. This race was traced in source, not executed against PostgreSQL.

### A07 — P2: Dispatch polling changes the query semantics and can overwrite another view

**Evidence:** [DispatchList.razor.cs](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Pages/Dispatch/DispatchList.razor.cs:151). Initial loading adds `includePlanned=true` for Table/Papers; minute polling omits it and replaces `_data`. The stale-response guard checks page/search/truck but not view.

**Impact:** Planned loads disappear after an automatic refresh. A response started in one view can overwrite data after a fast view switch.

**Correction:** One board-query builder and captured request identity for initial load, manual reload, and polling. The identity must include every query-affecting input.

**Acceptance:** Table/Papers retain planned loads after a simulated minute; delayed responses from an old view cannot replace the new one.

### A08 — P2: Expired ETA can continue claiming “On time” after refresh failures

**Evidence:** [ArrivalEstimate.razor.cs](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Shared/ArrivalEstimate.razor.cs:15). Pending-update memory is bounded, but `DisplayEta` falls back to current `Eta` without checking its `ValidUntil`. Failed polling preserves that current object.

**Impact:** A prolonged outage can present an expired estimate as a fresh scheduling claim.

**Correction:** Use one freshness policy for both current and retained estimates. Keep the previous ETA during the permitted recalculation grace, as requested, without indefinitely preserving the “On time” claim. Reserve the layout space so this does not reintroduce jumping.

**Acceptance:** Fake-clock tests cover valid, updating-grace, expired, failed-poll, completed-stop, and changed-dispatch states. No stale estimate is presented as current.

### A09 — P2: Same-version current-route metadata never reaches visible labels/popups

**Evidence:** [routeLayer.setPlan](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Scripts/fleetMap/routes/routeLayer.js:53) returns before replacing the plan when ID/version/tracking match. The server can refresh name, job, appointment, commodity, and notes without changing route geometry/version.

**Reproduction:** An isolated JS fixture supplied a new warehouse name with unchanged route identity. The marker retained `Old warehouse`.

**Correction:** Update stop metadata independently of geometry identity. Next-load metadata revisions already demonstrate this separation; current-route rendering needs an equivalent contract.

**Acceptance:** Renaming/rescheduling a stop updates its label and popup with zero geometry rebuild and zero provider request.

### A10 — P2: Searching can check “Trucks” while trucks remain hidden

**Evidence:** [FleetMap.razor.cs](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Pages/FleetMap/FleetMap.razor.cs:329). Search sets `ShowTrucks=true`, but the update only calls JS `setTrucks`, not its visibility operation. The truck layer rejects focus while hidden.

**Correction:** Give visibility changes one owner and synchronize the renderer whenever search enables the flag.

**Acceptance:** Uncheck Trucks, search for a truck, and choose it: the checkbox, visible markers, and focus all agree.

### A11 — P2: Fleet route previews silently truncate at 100 truck rows

**Evidence:** [RoutePreviewService](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Routing/Services/RoutePreviewService.cs:15) requests `PageSize: 100` once and ignores `HasNextPage`. Empty truck rows also consume that limit.

**Correction:** Page through eligible assignments or add a purpose-specific bounded projection that covers all preview-eligible rows. The synchronization operation already handles this board's pagination.

**Acceptance:** More than 100 active trucks, including empty trucks before assigned ones, does not omit a saved preview. Do not calculate new routes merely to satisfy this read.

### A12 — P2: Post-delivery fuel planning bypasses full-address resolution

**Evidence:** [FuelRegionPlanner](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Routing/Services/FuelRegionPlanner.cs:38) trusts imported coordinates for the next pickup; absent coordinates, it geocodes only `pickup.Address` without city/state/postal code/country.

**Impact:** Street-only input can fail validation, while a coarse imported point can select an incorrect onward fuel corridor.

**Correction:** Reuse the established `StopLocation` full-address/verified-point policy through a shared contract, rather than maintaining a second interpretation of a valid stop location.

**Acceptance:** Missing/coarse coordinates use complete locality inputs. Matching state/postal code supports locality resolution, but never substitutes for house/street confirmation.

### A13 — P2: A station with one unsuccessful lookup is never resolved through later imports

**Evidence:** [FuelStationSync](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/FuelStationSync.cs:30) skips every existing external ID. A no-result lookup creates a station with null coordinates; subsequent imports, even corrected ones, cannot repair it.

**Impact:** Coordinate-filtered station reads permanently omit that station through the normal import workflow.

**Correction:** Separate “existing and resolved” from “existing but unresolved.” Update identifying location fields and retry unresolved stations using persisted bounded backoff, not on every import.

**Acceptance:** Initial no-result followed by corrected input can resolve the station; repeated unchanged imports inside the retry window make no provider call.

## Performance and resource use

### A14 — P2: Financial board reads scale with lifetime truck history

**Evidence:** [DeadheadService.ReadAsync](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Features/Routing/Services/DeadheadService.cs:53) materializes every noncancelled historical load and its stop projection for displayed trucks. This runs on financial board reads even when the board index is cached.

**Impact:** A 12-truck page does not bound work: with 1,000 retained loads per truck, the query returns 12,000 loads plus stops before selecting predecessors in memory. This is an illustrative consequence of query shape, not a measured production dataset.

**Correction:** Put bounded predecessor-candidate selection in Infrastructure. Fetch endpoint details only for relevant candidates, retaining the existing ambiguous-history checks. Do not use an arbitrary recent-date cutoff that silently invents a different predecessor.

**Acceptance:** Query count, rows materialized, and allocation remain bounded by relevant candidate relationships as unrelated history grows. Compare database plans and p95 before/after before claiming a latency improvement.

### A15 — P2: Highlighting one stop invalidates all stop-layer data

**Evidence:** [sceneLayers.js](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Scripts/fleetMap/rendering/sceneLayers.js:87) creates one circle/text layer pair per stop. A changed stop snapshot creates fresh singleton arrays for all stops; IDs are based on group position rather than stable stop identity.

**Isolated measurement:** 100 distinct stops create 200 stop layers. Highlighting one stop creates 200 layer objects and 100 fresh data arrays. Of the layer IDs, 198 are reused but all 198 receive changed data identity; two IDs change with priority regrouping. Installed deck.gl detects those array changes and invalidates attributes.

**Correction:** Preserve stable per-stop identity/data or use a bounded set of priority batches with a rendering primitive that keeps each circle and its number together. A naive “all circles, then all numbers” rewrite would recreate the overlap defect the user reported.

**Acceptance:** Highlighting one load updates only relevant marker state; unrelated geometry/data remain reusable. Verify opaque overlaps, pickup/delivery distinction, current-stop priority, and hover promotion in the real browser. Layer-construction diagnostics are not FPS or GPU-memory measurements.

### A16 — P2: HTTP geometry reuse does not eliminate full WASM-to-JS geometry transfers

**Evidence:** [FleetMap.Payload.cs](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Pages/FleetMap/FleetMap.Payload.cs:18) serializes all restored legs for every successful route refresh. The JS entry point decodes/parses this payload before `routeLayer` rejects unchanged route identity. The selected-route polling interval is ten seconds.

**Impact:** Unchanged geometry still incurs serialization, allocation, bridge transfer, and parsing even when the server omits it from the HTTP response.

**Correction:** Separate geometry, progress, and metadata updates. Track the renderer's applied geometry revision and send the full shape only when that revision changes or the renderer is recreated.

**Acceptance:** An unchanged poll transfers no geometry over either HTTP or the WASM/JS bridge; ETA/progress/metadata still update. Recreating the renderer or A→B→A selection restores the correct cached shape. Byte counts and real latency remain to be measured.

### A17 — P3: Routine polling produces noisy Information logs

**Evidence:** [RequestDiagnosticsBehavior](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Application/Behaviors/RequestDiagnosticsBehavior.cs:41) logs every request in a Routing namespace, including fast successful polls. This contradicts the project's quiet-normal-polling rule. `IdentityService` also contains an unstructured `Console.WriteLine`.

**Correction:** Keep aggregate metrics; log slow/unexpected failures or deliberately sampled requests with structured templates. Preserve useful trace correlation without per-poll noise.

**Acceptance:** Ordinary successful polling produces metrics without an Information log per request. Unexpected failures are logged once at their boundary; credentials and raw provider payloads remain excluded.

## Operations, SCSS, and verification gaps

### A18 — P2: Deployment resolves a mutable image tag after building

**Evidence:** [deploy-server.sh](/Users/antonarlazarov/Developer/projects/AMFTMS/deploy-server.sh:19) deploys `api:latest`, while Cloud Build pushes the same shared tag.

**Impact:** An overlapping build can move the tag between one invocation's build completion and deployment. That invocation can deploy another build's image.

**Correction:** Deploy the digest or unique tag produced by the specific build, and include its immutable identity in release output. No concurrent deployment was exercised in this audit.

**Acceptance:** Two overlapping build invocations cannot substitute each other's artifacts; rollback names a specific artifact.

### A19 — P2 operational risk: Gmail watch renewal/recovery is not managed in this workspace

**Evidence:** [GmailWatchService](/Users/antonarlazarov/Developer/projects/AMFTMS/Server/Infrastructure/Services/Gmail/GmailWatchService.cs:21) returns expiration to the manual Admin command. No persisted renewal schedule or missed-notification recovery was found in workers/deployment configuration.

**Boundary:** An external scheduler may already exist; cloud state was not inspected. This is an unresolved operational dependency, not proof that the current watch is expired.

**Correction:** Verify and document the external owner, or persist expiration and implement bounded renewal/recovery. Google requires renewal at least every seven days and recommends daily renewal. [Google Gmail push documentation](https://developers.google.com/workspace/gmail/api/guides/push#renew_mailbox_watch).

**Acceptance:** Expiration, last successful notification/import, and renewal failure are observable. Dropped notifications can be recovered without unbounded reprocessing.

### A20 — P3: Theme roles duplicate primitive palette values

**Evidence:** [base/_themes.scss](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Styles/base/_themes.scss:5) repeats values already defined in `_colors.scss`, including primary and neutral colors.

**Impact:** Changing a primitive does not consistently change the roles that visually use it. Two catalogs must be maintained in parallel. Existing semantic variables and theme switching still work; this is not a claim that themes are impossible.

**Correction:** Define primitive values once and derive semantic role maps from them. Retain explicit role-specific values where intentionally different. Components consume semantic roles, not palette indexes.

**Acceptance:** A primitive change propagates to dependent roles; both themes expose the same semantic contract. Named `pg()`, typography, radii, and control tokens remain the standard. Exact map/SVG geometry and breakpoint dimensions are not blindly converted into spacing tokens.

### A21 — P3: Unused legacy truck DOM styles are still shipped

**Evidence:** [components/_index.scss](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Styles/components/_index.scss:8) imports legacy truck marker/popup styles. Their selector families have no consumers in maintained Scripts, Pages, Shared, or Layout source after GPU rendering replaced that DOM.

**Impact:** Small unnecessary CSS output and misleading ownership: editing those files cannot change the current GPU truck marker.

**Correction:** Confirm no external consumer, then remove obsolete partials/imports and update renderer documentation. Import-reachability tests alone cannot identify selectors with no DOM consumer.

**Acceptance:** Current map/popups remain correct and old selector families are absent from compiled output. Do not claim a significant performance gain from this small cleanup.

### A22 — P3: Page title positioning still varies by page wrapper

**Evidence:** Dispatch adds top padding; Dispatch/Users use a centered 1,440px body, Settings a centered 1,040px body, and Fleet Map a full-width body. Relevant ownership starts at [dispatch/_board.scss](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Styles/pages/dispatch/_board.scss:3).

**Browser CSS fixture at 1,800×900:** Fleet heading x=216/y=24; Dispatch x=268.5/y=32; Users x=268.5/y=24; Settings x=468.5/y=24. Font size was consistently 28px. This measured actual compiled CSS with representative wrappers, not authenticated full-page screenshots.

**Correction:** One page-header placement outside optionally width-limited content. Keep narrower form content where useful rather than forcing every page's body width to match the map.

**Acceptance:** Page headings share desktop/mobile alignment and vertical rhythm; content can still use deliberate readable widths.

### A23 — P2 verification gap: Source-pattern tests do not execute important UI races

**Evidence:** [nextLoadIdentity.test.js](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/tests/architecture/nextLoadIdentity.test.js:5) reads C# files and asserts source patterns. It does not execute selection/toggle events or delayed responses. Existing manual lifecycle probes do not cover all Next Loads, board refresh, and authentication interleavings. Database tests use SQLite/EnsureCreated rather than PostgreSQL migration/concurrency execution.

**Impact:** Architecture tests and a green unit suite can coexist with A05–A10. This explains a coverage gap, not a claim that existing tests are useless.

**Correction:** Keep fast structural guards and add executable component/HTTP/browser tests at the relevant boundaries. Use controlled clocks and delayed responses. PostgreSQL-specific invariants require a separately approved safe validation target; do not run mutation/concurrency experiments on the shared production database.

**Acceptance:** At minimum: Next Loads on/off/on; truck A→B→A; reordered responses; current-load exclusion; continuous stop numbering; overlapping opaque markers; hover promotion; same-version metadata edits; unchanged polling; ETA grace/outage; Dispatch view refresh; account change during 401. Use auto-retrying assertions rather than arbitrary sleeps for normal UI waits.

### A24 — P2 release-gate gap: The Client deploy script does not enforce the documented gate

**Evidence:** [deploy-client.sh](/Users/antonarlazarov/Developer/projects/AMFTMS/deploy-client.sh:7) runs Client tests and ordinary publish, but not the required full suite or warning-as-error Client build.

**Boundary:** The manual prerequisite may have been followed on a particular release. The defect is that the release entry point does not enforce it.

**Correction:** One release verification command, shared by local deploy and CI, checks the full suite, strict build, artifact integrity, and intended immutable release identity. Keep affected-category runs for ordinary iteration.

**Acceptance:** The deploy entry point cannot publish after a failed required check. Framework boot resources and dynamic imports are checked against the exact staged Client artifact, not a mixture of old and new outputs.

### A25 — P3: Next-load error text survives a successful unchanged response

**Evidence:** [FleetMap.NextLoads.cs](/Users/antonarlazarov/Developer/projects/AMFTMS/Client/Pages/FleetMap/FleetMap.NextLoads.cs:66) returns for `Unchanged` before clearing `_nextLoadsMessage`.

**Correction:** A valid successful response clears transient error state regardless of whether geometry changed.

**Acceptance:** Failed refresh followed by successful unchanged refresh removes the error without retransmitting geometry or clearing visible cached routes.

## What is already worth preserving

- Thin MediatR controllers and explicit Application/Infrastructure contracts. Fallback authorization protects endpoints without method-level attributes; absence of `[Authorize]` on one method is not evidence of an anonymous API.
- Persisted active-user role checks, account lockout, refresh/security-stamp validation, logout revocation, and Gmail notification identity/audience/mailbox checks.
- Server-owned financial formulas and persisted rates/deadhead input signatures. Do not move RPM arithmetic into Client views.
- Saved geometry, distinct future-load connection identity, request budgets, cancellation/version guards, and bounded rendering caches. Their boundaries need the corrections above, not removal.
- Feature-based JS organization and shared map/provider abstractions. No new confirmed old-truck response race was found in the recent Next Loads selection guards.
- SCSS base primitives/semantic roles, named spacing/type functions, shared controls, and feature/page ownership. The structure is usable; clean duplicated/dead ownership instead of creating more folders.
- Categorized tests and architectural guards. They shorten iteration and must remain, supplemented by behavioral checks.

## Best-practice alignment and recommended design

### 1. Separate data by what invalidates it

Treat route geometry, stop metadata, tracking/progress, ETA freshness, and presentation selection as separate revisions/state. A GPS deviation can affect the active remaining route without invalidating a future delivery→pickup connection. Editing a warehouse name should update text without requesting or transferring geometry.

For Blazor, every incomplete await is a reentrancy boundary. Capture the full request identity and validate it after awaiting; cancellation alone does not guarantee that an already-completing response cannot be applied. This is the relevant principle behind A05 and A07, not a reason to introduce a global state framework. [Microsoft Blazor synchronization context](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context?view=aspnetcore-10.0).

### 2. Bound database work before adding caches or indexes

Project only required data, bound candidate row counts without changing business meaning, and inspect the actual query plan before choosing indexes. A narrow projection over all history still grows with all history. Measure query duration, materialized rows, allocation, and lock wait separately. [Microsoft EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).

### 3. Keep renderer data identity stable

Creating a new deck.gl layer object is not, by itself, the central problem. Changing its `data` identity can rebuild attributes/buffers. Reduce unnecessary data changes and layer count, and use explicit update triggers where appropriate. Avoid deep equality over every route on every frame as a substitute for correct revisions. [deck.gl performance guide](https://deck.gl/docs/developer-guide/performance), [Layer API](https://deck.gl/docs/api-reference/core/layer).

### 4. Treat external calls and secrets as independently owned resources

Reserve provider budget durably before dispatch; cache success and bounded failures by meaningful input signature. Track requested/attempted/succeeded/failed calls separately from cache hits and reconciliation. Do not let transaction rollback make a sent request free in the local ledger.

Keep secrets outside generated artifacts and centralize access, audit, and rotation policy. A secret filename exclusion is a packaging guard, not a complete secret-management system. [OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html).

### 5. Test boundaries, not just implementation text

Use unit tests for deterministic business formulas and signatures; component/HTTP tests for state and authorization; browser tests for real interaction/rendering; dedicated diagnostics for performance. Keep source checks for forbidden dependencies/tokens, but do not count them as proof of event ordering. [Microsoft integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0), [Playwright assertions](https://playwright.dev/docs/test-assertions).

## Ordered remediation plan

1. **Protect data and identity:** A01–A06. Add failing regressions first; avoid broad persistence changes without a reviewed migration and safe verification plan.
2. **Unify update semantics:** A07–A13 and A25. Share query/location/freshness ownership; preserve requested no-jump ETA and map behavior.
3. **Remove verified redundant work:** A14–A17. Establish baseline row/byte/layer metrics, then optimize one boundary at a time. Do not claim production speedups from unit-test timing.
4. **Harden delivery and operations:** A18–A19 and A24. Pin artifacts, enforce release gates, verify Gmail renewal ownership, and document recovery.
5. **Finish standards and behavior coverage:** A20–A23. Remove dead styles, derive semantic colors from primitives, align page headers, and add executable scenario coverage. Update older renderer/release documents that still describe removed DOM/spatial-grid implementations.

## Definition of done for the stabilization release

- Each corrected invariant has a regression test or an explicitly documented manual/external check.
- The affected categories and architecture tests run during implementation; the full suite and strict Client build run for the release.
- Unchanged polling performs zero paid route calls and zero full-geometry transfers on either transport boundary.
- Inserting a load changes only affected predecessor/successor connections; current GPS deviation does not recalculate unrelated future connections.
- Truck switching/toggles, opaque overlapping markers, pickup/delivery distinction, label centering, current/future priority, and ETA retention are verified in the visible browser.
- Baseline/after measurements distinguish HTTP bytes, WASM/JS bytes, materialized history rows, provider attempts, lock wait, JS allocation, and real frame time. No absolute speed or zero-leak claim is made without its measurement.
- The exact staged artifact passes boot/dynamic-import checks and contains no credential file; deployment references its immutable identity.
- Remaining operational risks are named with an owner and verification method, not hidden behind “all tests passed.”

## Separately tracked operational decisions

- Legacy accounts without a role currently default to Admin. This is documented/tested compatibility, not an accidentally unprotected controller. Migrate deliberately to explicit roles before changing this behavior.
- Data Protection keys lack application-level encryption at rest; backup/table access controls were not evaluated. This is already documented release debt.
- Migration creation/application and production startup migration need an explicit release policy. No current migration state was inspected or changed.
- Long browser/GPU memory soak, production query plans, multi-replica quota/concurrency behavior, and provider billing reconciliation remain unmeasured. A clean unit-test run cannot close these items.
