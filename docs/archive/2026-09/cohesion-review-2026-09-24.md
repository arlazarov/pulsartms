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

## Reviewed and deliberately left

- **Five `Mark`/`Take` timing helpers:** each binds its own operation name
  in four lines. Consolidating would churn dozens of call sites with no
  owner gained.
- **`BaseRouteService`:** one owner (the base road), five dependencies,
  parts by step. `Signatures` is a set of pure static functions other
  owners call; a static type of its own is possible, low value.

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
  inbox poll. The unread notice itself is two queries.
- **`AutomaticPlanningTests`** is one partial test class across 15 files
  (5,372 lines) held together by a nested fixture. That fixture should be a
  support type. Tonight's reroute tests followed the existing pattern.
- **From yesterday, still open:**
  - findings 1–2 (the fuel pipeline written twice; the reserve rule) wait
    for a product decision;
  - 3, 6, 8, 9, 11 and 12 are untouched;
  - 4 is folded, but `ReadAsync`'s nine reads are still one method.
- **Decisions to take, not code to write:**
  - `DispatchWorkspaceReader.Ordinary()` is exact-case, narrower than
    `SourceWords.MovesCargo`;
  - the receipt refusal answers 409 on create but the default status on
    update and correction.

## Not reviewed tonight

Everything not in the table above. Outlines are not full reads: the
FuelPlanningService, FleetMap, DispatchList and fleetMap.ts rows looked at
members, fields and dependencies, not every method body. In particular:
- `RoutePlanStorage` (a static codec in six parts, not read);
- `FuelPlanEditor`, `DispatchDetails` and the other Client pages;
- browser modules other than the two above (`stationLayer.ts` is the other
  known whole-screen module);
- style contents;
- Infrastructure adapters, tools and scripts;
- the tests beyond those touched.

Four production server files sit at 381–399 lines, just under the old limit:
FuelPlanningService.Editing, GoogleAddressGeocoder, EtaChainInputsService,
FuelPlanningService. They were not read for signs of trimming. CSharpier
fixes their formatting, so trimming there would show as removed comments or
moved methods, not as compressed code.
