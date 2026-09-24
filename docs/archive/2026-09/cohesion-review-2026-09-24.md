# Cohesion review, 2026-09-24 (night)

This follows the owner's decision to drop line limits
([cohesion review](../../architecture/cohesion-review.md)). The review
started from yesterday's
[structure review](source-structure-review-2026-09-23.md), none of whose
findings had been acted on. Local only.

## Coverage: what was inventoried and what was read

**Machine inventory, not reading.** `node scripts/source-inventory.mjs` lists
every tracked maintained source: 2,476 files in server Domain, Application,
Infrastructure and API, server tests, client C#/Razor, client C# tests,
browser modules, client JavaScript tests, styles, tools, scripts and root.

- **Excluded, with reason:** 144 generated EF migrations, and 1 type
  declaration file.
- **Not in the inventory at all:** build output (`bin`, `obj`, `artifacts`,
  `node_modules`), which git does not track.
- **Signals it reports:** partial types across files, the smallest part of
  each, constructor-injected dependencies, and the longest files per area.

**Read by hand, tonight.** Nothing outside this list was read tonight. The
counts below are the inventory's; they are not a claim that every other file
was reviewed.

| Group | Read for | Outcome |
| --- | --- | --- |
| `Application/Storage` (FileStore, its former partial, StorageConnections, StorageRoots, StorageReconcileOperation) and InboundMediaOperation | owner of connection access | New owner `StorageTargets` |
| `DispatchWorkspaceReader` and its `.Review` part | residue part | Folded back |
| `RouteChoiceService` and its `.Publication` part | residue part, forwarding call | Folded back; direct dependency |
| CreateDispatch, UpdateDispatchWorkspace, CorrectDispatchStop | copied receipt rule | New owner `DispatchWorkspaceReceipts` |
| Five `Mark`/`Take` timing helpers | duplication | Reviewed, left (below) |
| `RoutePlanningService` (7 files, 13 dependencies) | owners, dependency map per part | Proposal, not done |
| `BaseRouteService` (6 files) | parts vs owner | Reviewed, cohesive |
| `Client/Pages/Messages/Messages.razor.cs` | mixed concerns | Proposal |
| `RoutePlanningService.ReadReferenceAsync` / `SavedRouteReader` | measured idle cost | Proposal; parse cost cut separately |
| Size checks and guides: ServerStructureTests, scriptStructure.test.js, styleStructure.test.js, AGENTS.md, source-size.md, Scripts/Styles READMEs, ui-controls.md | policy | Replaced (below) |
| `FuelPlanningService` (6 files, 13 dependencies), member outline | owners | One owner; yesterday's finding 1 stands |
| Client `FleetMap` page (12 files, 81 fields), member and field outline | mixed owners | Proposal: next-loads owner |
| Client `DispatchList` (2 files), member outline | mixed owners | Proposal: board-state owner |
| `Client/Scripts/fleetMap/fleetMap.ts`, outline | composition root | Reviewed, fine |
| `Client/Scripts/fleetMap/routes/*` and commit `093c5cb4` | split for the limit? | Split by responsibility, fine |
| Style and script file names across the tree | limit-driven names | None found |
| Inbox and unread-notice handlers | round trips | Finding: duplicate user read |
| `GoogleAddressGeocoder` and `GoogleAddressValidation` | borrowed internals | New owner `GoogleAddressMatching` |
| Every read-cache group literal (about fifty sites) | missing owner of names and of the work-changed set | New owner `ReadGroups` |
| Server Application, Infrastructure and API, scanned for classes whose public members only forward to one dependency | forwarding layers | None needless (below) |
| `RoutePlanStorage` (6 parts), outline | owners | One owner; static with I/O (below) |
| `Client/Shared/Fuel/FuelPlanEditor` (919 lines), outline | owners | Cohesive; carries finding 11 |
| `DispatchWorkspaceReader.ReadAsync`, outline | extraction | Left (below) |
| `tools/FleetLoadProbe/Program.cs` | shutdown | Fixed |
| `TomTomRoutingProvider` (5 parts), outline | owners | Proposal: routing call ledger |
| Client `DispatchDetails` (612 lines), member outline | owners | Cohesive: one page and its unsaved-changes protocol |
| `Client/Services` (11 files), sizes and names | owners | Single-purpose; not read in full |
| `Server/Application/Features` folders against `module-ownership.md` | mixed owners | Match; fuel planning is Routing's by design |
| `EtaChainInputsService` (3 parts, 10 dependencies), outline | owners | Cohesive: one input assembler over several sources |
| WhatsApp Cloud adapter (2 parts), outline | owners | Cohesive: one provider adapter; Media is a real concern split |
| `SyncDispatchesCommandHandler` (2 parts) and its matchers, outline | owners | Acceptable: helpers already separate owners |
| `FuelHorizon` (2 parts), outline | owners | Cohesive: the roads ahead for fuel and its batched reads |
| Execution switch commands (`PlanSwitch`, `SwitchParticipantActions`), outline | owners | Cohesive: one command each, parts by step |
| `Client/Scripts/fleetMap/stations/stationLayer.ts`, outline | owners | Cohesive: composed from price-book, plan and popup modules |

## Policy change

Removed, and only these:
- the server 400-line review with reviewed maximums;
- the browser 300-line and shrink-only budgets;
- the stylesheet 280-line rule.

Every layer, dependency, ownership, authentication, tenant, style-token and
behavior check is unchanged. `ui-controls.md` had no size rule; its size
*tokens* are a design vocabulary and stay. Commit `35e466c3`.

## Changes, each its own commit

1. **`StorageTargets` owns reaching a company's storage** (`ea4f9ed3`).
   - *Before:* `c2f455e5` (earlier tonight, mine) moved connection
     resolution into `FileStore.Connections.cs` to pass the line rule. Four
     classes reached into FileStore only for that plumbing:
     StorageConnections, StorageRoots, StorageReconcileOperation and
     InboundMediaOperation.
   - *Now:* the default connection, a connected connection, provider,
     target (the only place a secret is unprotected) and the size a
     provider accepts have their own class. FileStore keeps the stored-file
     protocol.
   - *Test fix:* four reconciler tests broke because their hand-built scope
     lacked the new service. They carry `Category=Dispatch`, so my storage
     run missed them; fixed in `40d75b14`.
2. **`DispatchWorkspaceReader.Review.cs` folded back** (`b3df5a9e`). Two
   helpers were split off only for the count.
3. **`RouteChoiceService.Publication.cs` folded back**, and profiles are read
   from the injected owner rather than through
   `RoutePlanningService.ProfileAsync`, the same scoped instance
   (`8b4cc333`).
4. **`DispatchWorkspaceReceipts`** owns "same request, same person, same
   load" for workspace retries (`c90785b7`). A new test covers the refusals
   no test pinned before.
5. **Not structural, found on the way** (`aa4c4e04`): the storage auditor's
   PostgreSQL test passed a random GUID as the page cursor and passed about
   half the time. It was a test bug of mine from `5d2748bb`.
6. **`GoogleAddressMatching`** owns the address-text rules both Google
   adapters judge answers by; the geocoder keeps transport, cache and its
   own response (`b0cb06f5`, yesterday's finding 8).
7. **`ReadGroups`** names the seven read-cache groups and the set a work
   change makes stale, which was written out eleven times. A misspelt group
   is now a compile error; `ReadGroupsTests` pins the names, which other
   instances receive (`e7ca05ad`, finding 6).
8. **Load probe shutdown** (`647ec3a2`): the probe exits in about a second
   on SIGTERM instead of being killed at 90 s.

## Reviewed and deliberately left

- **Five `Mark`/`Take` timing helpers:** each binds its own operation name
  in four lines. Consolidating would churn dozens of call sites with no
  owner gained.
- **`BaseRouteService`:** one owner (the base road), five dependencies,
  parts by step. `Signatures` is a set of pure static functions other
  owners call; a static type of its own is possible, low value.
- **Forwarding:** the scan found three single-dependency classes. Each
  earns its place: a MediatR handler (the layer boundary requires one), a
  cache holder that owns its key and loader, and an Infrastructure
  implementation of an Application interface.
  `RoutePlanningService.ProfileAsync` is a forward, but it is a member of
  `IPlannedRouteReader`, which fuel planning uses. Removing it is a
  contract change.
- **`RoutePlanStorage`:** one owner, the saved plan's storage mapping, with
  parts by step. It is static, yet does database I/O and keeps a static
  `ConditionalWeakTable` of fingerprint captures. It could become an
  instance owner; low priority.
- **`DispatchWorkspaceReader.ReadAsync`:** its tail is one
  read-and-project pipeline. Cutting it into a function with eight
  parameters would be a split, not an owner.
- **Yesterday's finding 12** (`stationLayer.ts` at its shrink-only budget)
  no longer applies: there is no budget.

## Proposals, with evidence (not started)

- **`RoutePlanningService` read vs write.**
  - *What it holds:* build, automatic tracking and completion change the
    saved plan under one per-truck gate, sharing almost every dependency.
    Extracting tracking alone would split that writer. Reading
    (`GetAsync`, display reference, stop enrichment) is the other side,
    with its own dependencies (displays, region options). `ProfileAsync`
    and `LocationAsync` are forwards used by six other classes.
  - *Proposal:* a plan reader separate from the plan writer. The read path
    has many callers; stage it with call-count tests on the fleet-efficiency
    read paths.
- **The display reference is re-read on every plan read.**
  - *Measured* (traced idle fixture): reading the load's base road to attach
    a display reference was the largest single allocation owner. It went
    from 880 MB to 364 MB per ~190 s after the converter in `a972cb54`.
  - *Cause, from the code:* a build from the truck's current position
    (`RoutePlanningService.BuildAsync`) calculates only the road from the
    truck, never the load's base road. When the background base-route
    operation later writes that road, every read finds it
    (`ReadReferenceAsync`), transfers and parses the whole row, attaches it
    in memory, and never keeps it. The row is rewritten in place (same id,
    new JSON), so its version is its input hash plus calculation time.
  - *Two ways out, neither started:*
    - Attach and save the reference once, in the writer. That changes the
      stored geometry: a chunk write, and possibly the geometry revision
      that fuel's saved-road validation compares. Not verified.
    - Remember the parsed reference by row version. It must be handed out
      as a fresh shell per read, because display trimming
      (`PlanningReadService.TrimForDisplay`) reassigns its legs and points.
  - *Either one* needs a controlled-interleaving test under the consistency
    contract.
  - *Next morning:* the second way was built and measured, then reverted.
    The shared 16 MiB display budget cannot hold fixture-size roads. See
    [display reference cache](display-reference-cache-2026-09-24.md). The
    writer-side fix remains the way out.
- **`Messages.razor.cs`:** the composer (draft, send, template, file,
  retry, claim) is its own component with its own state and lifecycle. The
  formatting helpers could be shared with `MessageItem`.
- **Client `FleetMap` page:** one component holds 81 fields across 12
  files. The next-loads feature (`NextLoads` + `NextLoadDetails`, 724 lines,
  17 fields, its own polling and revision state) is its own owner inside the
  page. Extracting it touches the 5,653-line component test suite.
- **Client `DispatchList`:** board load, search, and separate telemetry,
  planning, HOS and enrichment polling, each with its own lifecycle.
  Candidate: a board-state owner, possibly shared with FleetMap's polling.
- **Inbox round trips:** the driver-group scope (added tonight) reads the
  user row that `Inbox.UserAsync` has just read, one remote round trip per
  inbox poll. The unread notice itself is two queries. *Resolved by
  `e7aa35aa`*, which serves the group choice from the read cache. This
  proposal was written before that commit.
- **`TomTomRoutingProvider`.** Yesterday's review called it "large but
  fine"; against tonight's criteria it is two owners.
  - *What it mixes:* the TomTom transport and parsing, and a database-backed
    call ledger: reservations, daily and per-minute limits, and the result
    cache in `RoutingApiCalls`. The ledger belongs to no one provider.
  - *Why it matters:* the September 22 audit measured seven round trips per
    provider call on that path, behind a process-wide `SemaphoreSlim(1,1)`.
    That was the 3.2 s fuel reset.
  - *Also:* `GeocodeAsync` only forwards to the injected Google geocoder.
  - *Proposal:* a routing-call ledger owner in Infrastructure behind the
    existing interfaces. It touches provider-cost guards, so it needs its
    own review and call-count tests.
- **`AutomaticPlanningTests`** is one partial test class across 15 files
  (5,372 lines) held together by a nested fixture. That fixture should be a
  support type. Tonight's reroute tests followed the existing pattern.
- **From yesterday, still open:**
  - findings 1–2 (the fuel pipeline written twice; the reserve rule) wait
    for a product decision;
  - 3, 9 and 11 are untouched (6 and 8 were done tonight; 12 is gone with
    the budget);
  - 4 is folded, but `ReadAsync`'s nine reads are still one method.
- **Decisions to take, not code to write:**
  - `DispatchWorkspaceReader.Ordinary()` is exact-case, narrower than
    `SourceWords.MovesCargo`;
  - the receipt refusal answers 409 on create but the default status on
    update and correction.
  - **Stop-job words (yesterday's finding 3), catalogued tonight.** About 25
    places read a stop's job outside `SourceWords`, in three ways:
    - exact-case `"Pick Up" or "Pickup"`: the workspace rules and stop
      display, Shipments, DispatchStopWorkspace;
    - case-insensitive `Equals`: DispatchTable, DispatchLoadDialog;
    - `Contains("pick")`: the FleetMap route label and ArrivalEstimate.

    The workspace rule that rejects "Pickup" applies only to stops the
    editor creates, and the editor writes the canonical words, so imported
    spellings pass. No live bug was found; the readings differ only for
    spellings not seen today. Load-status checks mostly accept "cancelled"
    and "canceled"; the leg and switch statuses compared to "cancelled" are
    PulsR's own vocabulary, not imported words.

## Not reviewed tonight

Everything not in the table above. Outlines are not full reads: the
FuelPlanningService, FleetMap, DispatchList and fleetMap.ts rows looked at
members, fields and dependencies, not every method body. In particular:
- Client pages other than Messages, FleetMap, DispatchList, DispatchDetails
  and the fuel plan editor;
- browser modules other than fleetMap.ts, stationLayer.ts and the route
  layer split;
- style contents;
- Infrastructure adapters other than Google, TomTom and WhatsApp, tools and
  scripts;
- the tests beyond those touched.

Four production server files sit at 381–399 lines, just under the old limit:
FuelPlanningService.Editing, GoogleAddressGeocoder, EtaChainInputsService,
FuelPlanningService. They were not read for signs of trimming. CSharpier
fixes their formatting, so trimming there would show as removed comments or
moved methods, not as compressed code.

## Morning continuation (September 24)

Read by two read-only review passes plus my own checks. Each finding named
below was verified in the source before it was acted on or recorded.

**Read or outlined:**
- *Infrastructure* (everything except the Google, TomTom and WhatsApp
  adapters and generated migrations):
  - read in full: DependencyInjection, the three AppDbContext parts, every
    store and reader under Persistence, all Samsara, Identity,
    Synchronization and Gmail files, BaseApiService, TorqueApiService,
    TorqueDispatchProvider, the storage adapters, BVD, IFTA, the health
    checks, LocalDriverMessaging;
  - by outline only: the EF configurations, provider models,
    ProviderJson, ResponseStream, RuntimeMemoryReader, BankOfCanada,
    RouteRegionLookup.
- *Client pages* other than Messages, FleetMap, DispatchList,
  DispatchDetails and the fuel plan editor:
  - read in full: DispatchStopCorrection, DispatchStopWorkspace,
    MileageMovementEditor, DispatchResourceOptions,
    DispatchWorkspaceStopDisplay, DispatchMileageTotals,
    DispatchAssignmentReview, DispatchBilling, DispatchRig(Status),
    DispatchStopTransfer, LoadAdjustments, Border, FleetSettings,
    FuelSendingSettings, Settings, PersonalSettings, Home;
  - read in large part: DispatchPlanning, DispatchMileageBreakdown,
    DispatchNumberSettings, IntegrationSettings, Customers, DispatchBroker,
    DispatchStopForecast;
  - the rest by outline only.
- No residue partial and no split made for the old limits was found in
  either area. Client Razor and C# were never under a line limit.

**Fixed:**
- **Layer violation** (`7a0b70c9`). `CacheInvalidationWorker` in
  Infrastructure took the concrete `CacheInvalidationRelay`. The namespace
  regex missed it because `Application.Caching` holds interfaces and
  classes alike. The relay is now behind `ICacheInvalidationRelay`. A new
  architecture check reads every Infrastructure constructor and fails on
  any concrete Application class. It found only this case.
- **ETA enrichment's saved-plan metadata read twice** (`da34b0a6`). The
  twenty commands were mapped on the load fixture by statement and calling
  frame. Warm enrichment now takes 19.

**Verified and recorded, not changed:**
- **Settings page 409.** `DispatchNumberSettings` and
  `FuelSendingSettings` edit one record, each with its own revision. Saving
  one makes the other's next save a conflict. No update is lost (null
  means unchanged, and a stale revision is refused). The page also fetches
  the record three times. *Proposal:* one owner of the page's dispatch
  settings state.
- **HOS provider cache.** `SamsaraDriverHosProvider` keeps a process-wide
  key and a static gate while credentials are per company. The only
  production caller refreshes and never reads the cache, so nothing leaks
  today. The non-refresh path would cross tenants if it were used.
  *Proposal:* remove that path or key it by company.
- **ETA enrichment's remaining repeats.** The three `Dispatches` reads are
  `ExecutionLoads.ReadAsync` from the board, from the ETA itinerary (fresh
  snapshot) and from deadhead history's execution batches.
  - *Done later that day:* `64f9e8b8` joins the loads to the link read, so
    each call is one round trip shorter (warm enrichment 19 to 16).
  - *Still a proposal:* the itinerary and history reading their links and
    handovers separately inside one snapshot. They read different sets of
    legs. The board's read is outside the snapshot and cannot be reused.
- **Plan-preparation "busy" answer outside planning.** A handler that lets
  it escape becomes a logged 500 (see the preparation record). Not every
  handler has been audited.

**Proposals from the passes, not verified line by line:**
- the leased-queue claim and prune SQL, written three times
  (`SourceRoadStore`, `PlanningRefreshStore`, `ExecutionPlanningStore`);
- the tenant guard `Company()`, copied six times; two `DatabaseInstant`
  helpers that differ on UTC;
- `TorqueApiService` re-implementing the base send path and reading
  options from configuration;
- `SessionStartupFilter` composing the API pipeline from Infrastructure;
- `DatabaseInitializer` running a periodic loop outside `ApplicationWorker`;
- `CloudStorageFileStorage.OpenAsync` and `DeleteAsync` skipping
  `Owned()`'s key-length check;
- carrier-specific values hard-coded (Samsara tag, Gmail topic and label);
- Client:
  - the allocation editor inside `DispatchMileageBreakdown`;
  - `Border.UseAssignment` belonging to `BorderResources`;
  - `FleetSettings` both hosting a layout and holding a form;
  - an apparently unused `DispatchRig` and its stylesheet;
  - a dead branch in `DispatchStopCorrection` (its "this side of the
    transfer" message can never show; the intended condition needs the
    owner);
  - a triple fetch in `MileageMovementEditor` that
    `DispatchResourceOptions` already does;
  - unused members in `Home`.

**The four files last night flagged as sitting just under the old limit**,
checked for trimming. Every commit to each was examined for removed
comments and moved methods.
- *No trimming found.* Four commits removed comments.
  - Three moved them, verbatim, with their code to a new owner:
    - `00b8cab9`, fuel rules;
    - `438df4a3`, the ETA split;
    - `b0cb06f5`, Google matching.
  - `27f57a1d` re-worded one explanation into the planning policy that
    now owns "where a run ends", and its message says so.
- *`FuelPlanningService`* (six parts, 14 dependencies) is one owner, the
  truck's fuel plan.
  - Two parts are now past 400 lines: `FuelPlanningService` 420 and
    `.Editing` 415. The growth is this morning's hand-over rule, which
    belongs there. Keeping stops reuses the editor's pipeline rather than
    copying it.
  - The long `BuildCoreAsync` and `EditCoreAsync` are yesterday's finding 1
    (the pipeline written twice). They wait for the owner's decision.
- *`GoogleAddressGeocoder`* (260 lines) keeps transport, cache and its own
  response since `b0cb06f5`.
- *`EtaChainInputsService`* (382 lines, three parts) is one input
  assembler, as last night found. `da34b0a6` removed its doubled plan read.

**Still not reviewed:**
- client pages and shared components outside the lists above;
- browser modules other than those named last night;
- style contents;
- tools and scripts;
- tests beyond those touched.

