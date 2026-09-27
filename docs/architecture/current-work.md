# Truck current work: one server owner

Status: design with staged delivery. Stage 1 is implemented (46a64f4c, not
released). Later stages are proposals awaiting review. Read with
[fleet-efficiency.md](fleet-efficiency.md), whose ownership table and
consistency contract still apply.

## The question and its three facts

Every screen and job asks "which work is this truck on, and where is it in
that work". Three different facts answer parts of it. They must stay
separate:

- **Route-passed.** GPS tracking in the saved route plan reports
  `Tracking.AllStopsPassed` for the same truck, dispatch, leg, assignment
  revision and route inputs (`PlanningWorkPolicy.IsCompleted`). Planning
  moves on from such work, so fuel and ETA are prepared for the next load.
- **Business-completed.** Accepted execution says the work is done: the
  leg is completed or the accepted stops have delivery actuals
  (`LoadCompletion`). Only a person or an accepted reconciliation writes it.
- **Source status.** What the imported source says (`Dispatch.Status`,
  source actuals). It is an observation, not accepted state. `in_transit`
  in the source does not mean started, and a source delivery actual does
  not mean completed, until reconciliation accepts it.

A **conflict** exists when these disagree in a way a dispatcher must see:
the route is passed but the accepted work has no delivery, or the source
is ahead of accepted execution, or reconciliation cannot resolve the
source. A conflict is shown, never resolved by silently preferring one
fact.

## Proved and inferred: AMF1395 on truck 11006

Proved from data (Root, 2026-09-27):

- The source pickup is 9/25 and delivery 9/27; the trailers differ
  (44120, then 9P1175). Source status is `in_transit` although a delivery
  actual exists.
- The accepted leg 2f2cc129, revision 3, is still `planned` with no
  actuals and a "Source resources unresolved" review. Reconciliation stops
  at the resource check (`ExecutionSourceReconciliation`, the early
  `continue` after `Observe`) before it reaches actuals.
- Papers shows "Awaiting pickup Sep 25 / Next"; the normal Map shows 1412.

Proved by Root (read-only, 2026-09-27): the saved plan for the same leg
at revision 3 has tracking `AllStopsPassed = true`. So
`TruckPlanningInputsReader.CaptureAsync` counts 1395 as route-passed and
names 1412 current, and the Map follows that, while the accepted stops
have no actuals. This is route-passed work that is not
business-delivered (CW1), not a source fallback.

Inferred from code, not reproduced: Papers' phase and label come from
client derivations over the board row (see "Consumers"), not from the
server's current work.

Neither is a trailer switch; no design here invents one.

## Owner map today

`ExecutionWorkReader` decides which loads are work
(`ExecutionWorkRelevance.IsCurrentOrUpcoming`) and their order
(`WorkOrderKey`). `TruckItineraryReader` turns them into
`TruckWorkSegment`s. On top of that:

| Concept | Owner | Other deciders |
|---|---|---|
| Current work | `TruckPlanningInputsReader.CaptureAsync`: first `PlanningWorkPolicy.Candidates` item not route-passed (saved metadata overload). Exposes `CurrentWork`, `CurrentAssignmentRevision`, `CurrentSegment`, `PassedWork` | `PlanningReadService.ForItineraryAsync` (own loop, `RoutePlan` overload, `Resolve` before the completion check); `RoutePreviewService.ReadRowAsync` (own loop, stops at the first candidate without a plan); `EtaChainInputsService.Select` (own loop over a fresh itinerary); `AutomaticPlanningService`; `PlanningCurrency`; `NextLoadSelection` fallback; client `DispatchList.IsCurrent`/`LoadPosition` |
| Completed | `LoadCompletion` (sent as `Completed`) | `IsCurrentOrUpcoming` (board membership), the SQL filter in `GetDispatche` (Completed tab, no leg awareness), `NextLoadSelection` (manual only), GPS `AllStopsPassed` |
| Started | `ExecutionWorkRelevance.HasStarted`; leg status mapping in `ExecutionLoads` | `WorkSequencePolicy.Started` (copy), `RouteWorkProjection.Capture` (mapping without cancellation), client `DispatchBoardRow.InTransit` |
| Next loads | `WorkOrderKey`, `WorkSequencePolicy.Assess` | Map: `NextLoadSelection` (other sort, truck id only) |
| Phase | none on the server | client `LoadPhase`, `DispatchLoadCard.Phase`, `DispatchBoardRow.Planned` |
| Next stop, progress | `RouteStopTracker.Update` into the saved plan | `RoutePreviewService` recomputes progress from telemetry on read |
| Assignment revision | `TruckWorkSegment.AssignmentRevision` | legacy work: summary returns 0 (`RouteWorkProjection`), client compares `PlanningAssignmentRevision` |
| ETA | `EtaForecastService.RefreshAsync` | two readers: board and workspace read stored rows, map and summary read `EtaMemory` |

## Consumers

| Screen | Endpoint | Current work comes from |
|---|---|---|
| Cards | `/dispatch/board` + `/board/planning` | summary `DispatchId` matched in the client, else client fallback |
| Table, Papers | `/dispatch/board` | client fallback only: a view change clears summaries and planning refresh runs for Cards only |
| Completed tab | `/dispatch?status=completed` | own SQL rule |
| Map route | `/fleet/trucks/{id}/planning`, `/dispatch/{id}/planning/automatic` | `PlanningSummaryReader` (cache, then `ForItineraryAsync`) |
| Map previews | `/fleet/planning/previews` | `RoutePreviewService` own loop |
| Map next loads | `/dispatch/truck/{id}/next-routes` | current supplied by the client; `NextLoadSelection` otherwise |
| Messenger | `/messaging/conversations/{id}/context` | `DriverWorkOrder` over `TruckPlanningInputs`; client shows `Loads[0]` as current |
| Workspace | `/dispatch/{id}/workspace` | per load, no truck current work |

## Divergences

Found by reading code unless marked. Stage names the fix.

1. Cold, refused and `ForDispatchAsync` summaries named the first candidate,
   possibly route-passed, while the warm summary and Messenger named the
   next. **Stage 1, done.**
2. Four server selection loops with different completion overloads and
   `Resolve` order. A blocked earlier candidate refuses the whole summary
   or drops the truck from previews even when `CurrentWork` is later.
   Stage 2.
3. A summary refresh captures the itinerary three times; the result is
   built from cached inputs but stored under the fresh signature, so a
   stale capture can be stored as current. Stage 2.
4. The summary signature omits tracking, prices, road, deadhead, ETA and
   HOS; ETA publish, deadhead, base road and duty changes do not notify
   the summary after commit. Stage 4.
5. Table and Papers never apply the server's current work; the client
   fallback uses status and an unsorted first stop date. Stage 3.
6. Legacy work: summary revision 0 against client
   `PlanningAssignmentRevision`, and Messenger's `DriverWorkOrder.Same`
   compares a never-stored revision. Latent: 0 open no-leg loads with
   `PlanningAssignmentRevision > 0` (read-only check, 2026-09-27).
   Stage 3.
7. The Map's next loads use another order and truck matching, and fall
   back to their own current load when the client sends none. Stage 2.
8. Two ETA readers pick their root by different selectors; when roots
   differ the map shows no ETA. Stage 2 (root from `CurrentWork`) and
   stage 4 (one reader).
9. Four "completed" rules; a native leg completed while the source is
   `in_transit` appears in neither the board nor the Completed tab.
   Stage 4.
10. Board page (`includePlanned=true`, local date) and planning page
    (`includePlanned=false`, UTC) cover different trucks. Stage 3.

## Target: one truck work state

No new reader. `TruckPlanningInputsReader.CaptureAsync` stays the owner;
it already has the itinerary, saved metadata and profiles in one batch.
Stage 3 adds to each captured segment, from those same inputs:

- `Route`: `upcoming | in_progress | passed` (from saved tracking).
- `Business`: `planned | awaiting_pickup | in_transit | delivered |
  completed` (from accepted execution only).
- `Source`: the raw source status and `SourceReviewReason`, as read.
- `Conflict`: `none | route_passed_not_delivered |
  source_ahead_of_accepted | source_unresolved`.
- `Phase`: `earlier | current | next | upcoming`, with `completed` only
  from `Business`.

Selection keeps today's rule: current is the first candidate not
route-passed, so fuel and ETA follow the truck. Route-passed work that is
not business-completed stays visible with its conflict; it is never shown
as delivered or completed. Consumers (board rows in every view, Messenger
context, map truck payload, next-routes, workspace) carry these fields and
format them. The client stops deciding current, phase, started or
completed.

## Cross-consumer invariants

- **I1 One identity.** Every consumer naming a truck's current work names
  the owner's `CurrentWork` and `CurrentAssignmentRevision` from one
  capture, or says the value is retained and stale.
- **I2 Explicit requests.** A request for a named dispatch answers for
  that dispatch, including earlier work, and says whether it is current.
- **I3 GPS is not completion.** No consumer shows delivered or completed
  from route tracking.
- **I4 Source is not acceptance.** Source status or actuals never become
  started, delivered or completed without reconciliation; a conflict is
  shown the same way on every consumer, with no silent source fallback.
- **I5 Clients format.** Client code does not derive current, phase,
  started or completed.
- **I6 One revision.** Legacy and native revisions are normalized once by
  the owner; consumers compare the value they were sent.
- **I7 No repeated work.** The state is computed once per capture;
  consumers do not add database or provider reads to obtain it.

## Stages

1. **Done (46a64f4c).** `CurrentSegment` on the inputs;
   `PlanningSummaryReader.Scope` for cold and refused summaries;
   `ForDispatchAsync` compares with `CurrentSegment`. No new reads. Tests:
   `PlanningSummaryScopeTests` (fails when scope is the first candidate).
   The warm and actual refused agreement they did not prove is covered
   in stage 2a.
2. **One selection owner.**
   - **2a, implemented (not released).** The summary (`ForItineraryAsync`)
     and the previews read only the inputs' `CurrentSegment`. A plan read
     that finds that work passed after the capture is a change
     (`RoutePlanningException.Changed`), never a step to the next load:
     foreground readers capture again once through
     `TruckPlanningInputsReader.ReadAgainAsync`, which drops the cached
     entry only if it is still the version the reader saw, so readers
     that found the same stale entry share one capture. The background
     preparation builds from its one fresh capture, retries a change
     once, and before publishing checks the itinerary with a fresh read
     and the settings generation as it is now. `PlanningSummaryCache`
     leaves a preparation's lease with it when a commit, a capture or new
     inputs arrive, so changes during a preparation add one preparation
     after it; a lease ends after five minutes. A result prepared for
     other inputs than the entry's is no longer prepared again at once in
     a loop; a reader asking with the new inputs makes it due.
     Tests: `PlanningSummaryRefreshTests` (one capture per preparation,
     refusal names the current work, settings change and commit while
     building, stale cached inputs), `TruckRoutePreviewTests` (every
     reader agrees; a passed load needing review no longer refuses; a
     change mid-read; one recapture; shared recapture) and
     `PlanningSummaryCacheTests` (overlap counts, lease limit, no loop).
     Each fails under its mutation.
     Known limits: the interleavings are sequential and controlled, not
     parallel threads. A route refresh's `Capture` still takes an entry
     from a consumer preparing it; that is counted as two computations,
     not coalesced. Cross-process invalidation is not covered.
   - **2b, implemented (not released).** The rule is
     `PlanningWorkPolicy.ChooseCurrent` (and `IsPassed` for a caller that
     reads saved plans one at a time). The inputs owner, the ETA root and
     `PlanningCurrency` ask it; automatic planning starts at the inputs'
     current work and, when its own tracking passes it, captures again
     instead of stepping on. Work planning has passed no longer starts or
     ends the ETA chain, refuses automatic planning or fails a writer's
     currency check, even when it now needs review.
     `CurrentWorkOwnershipTests` lists the remaining direct uses of
     `Candidates` and `IsCompleted` with what each does.
     `EveryConsumerOfTheSharedInputsFollowsTheOwner` checks agreement and
     counts reads: ETA 1 batch, automatic planning 1 capture plus 2 lazy
     metadata reads, preview 1 capture after the writer's commit, summary
     0 after it, currency check 1 cold, 0 warm, 0 repeated.
     Known repeat: the writer's currency check re-reads, per plan and
     cached, metadata the capture read in its batch.
   - **2c, implemented (not released).** Next-routes takes the current
     work and the work after it from the planning inputs
     (`TruckPlanningInputs.Followers`, `PlanningWorkPolicy.Followers`):
     the itinerary's order (WorkOrderKey, as on the board: ties by load
     number, undated work by ship date), the board's membership (overdue
     work included) and one rule the old reader alone had (a planned leg
     of the load the current leg delivers is not next work).
     `NextLoadSelection`, `INextLoadRouteReader.ReadLoadsAsync` and the
     fallback to the client's current are removed. A client naming other
     work gets 409; inputs older than five seconds by the owner's clock
     (`CapturedWithin`) are captured once more first, so a client polling
     with a stale value costs at most one capture per five seconds.
     Tests: `NextLoadFollowersTests` (ties, missing dates, passed work
     needing review, stale client current with repeated requests,
     another truck's work, cold/warm counts, and two readers meeting at
     the load gate by handshake, not scheduling). Existing next-routes
     tests now mark their direct database edits as a writer does.
3. **Truck work state on every consumer.**
   - **3a, implemented (not released).** The board places each load from
     the planning inputs: `TruckPlanningInputs.Placements()` (built once
     per truck per request, looked up per row) gives current, next,
     upcoming, earlier or unplaced; a row read at another assignment
     revision is `stale`, one the inputs do not hold `unknown`
     (`DispatchResponse.WorkPhase`). `WorkConflict` says
     `route_passed_not_delivered` for earlier work execution has not
     completed. Cards, Table, Papers and the load dialog read both
     through `DispatchWorkPhase` and `DispatchBoardRow.Status`/
     `StatusTone`: a conflict reads as "Route passed · not delivered"
     (warning tone) and is filed with work under way, not "Awaiting
     pickup"; a stale place reads "Needs refresh", never a planned or
     upcoming phase. Removed: the client's `IsCurrent`, `LoadPosition`
     and `LoadOrder`, the card's position parameters, and the
     first-row fallback for the planning header.
   - **3b, implemented (not released).** The same placements reach every
     other view through `WorkPlacements` (phase, conflict, one list of
     conflicts per truck) and `PlanningWorkPolicy.AcceptedRevision` (a
     leg's revision or an older load's planning revision, one rule):
     - Messenger (`DriverWorkOrder`) no longer hides passed work: the
       current load first, then conflicts, then the work after it; loads
       beyond the list limit are counted with their conflicts. Older
       loads compare at their planning revision (divergence 6 closed for
       Messenger). The panel names the server's current load, not the
       first listed; filing offers only that load by default, follows it
       as it changes and keeps a load the dispatcher picked.
     - The map shows `AutomaticPlanningResult.WorkConflicts`, read with
       the same inputs as the summary, cold or prepared.
     - The load workspace places each accepted leg by its own truck and
       the load by its active leg (or its truck for an older load).
     - A load handed between trucks is placed under each truck by its
       own leg.
     A stale place reads "Needs refresh": nothing says a refresh runs.
     Gaps: map switch reset is defensive and not observable in the
     current rendering (its mutation survives); board, Messenger and map
     use the itinerary's membership as "not delivered" while the board
     also checks LoadCompletion - stage 4 makes them one rule; no
     call-count test on the placement pass; business and source states
     (`source_ahead_of_accepted`, `source_unresolved`) and one date basis
     for board and planning pages remain for stage 4.
4. **Completion and invalidation.**
   - **4a, implemented (not released).** The summary signature names the
     current work the inputs chose (identity and accepted revision), not
     only the itinerary and settings. A summary prepared for work that
     tracking then passed is retired as soon as the inputs move on -
     on processes the commit's summary notice never reaches, too - and
     a preparation from fresher inputs than a process' readers is not
     published under their older signature: all of that process'
     readers agree until its inputs are invalidated.
   - **4b, implemented (not released).** Three facts, three owners in
     Domain (`WorkCompletion.cs`): *cargo delivered* (`CargoDelivery`: the
     last Delivery/Drop Off among the stops the truck attends, overridden
     done, recorded, or confirmed by hand once the attended stops up to it
     are done), *truck work finished* (`TruckWorkCompletion`: cargo
     delivered and every attended stop after it done), and *load closed*
     (status). `LoadCompletion`: closed or truck work finished; a load in
     accepted execution is completed when all its legs are, whatever the
     source says. Itinerary membership reads truck work finished, so a
     load delivered with a trailer still to drop stays the truck's work
     ("Delivered · finishing" on the board, `DispatchResponse.
     CargoDelivered`). The conflict for passed work says which fact is
     missing: `route_passed_not_delivered` or `route_passed_work_open`.
     The Completed tab's filter (`CompletedLoads.Filter`) is the same rule
     in SQL - driver-only stops before the start and while a confirmed "No
     truck" state carries on, the dispatcher's action as the job, legs for
     loads in execution - and `CompletedLoadsParityTests` checks both
     against expected answers, shape by shape, on SQLite and PostgreSQL.

     | Shape | Cargo delivered | Completed |
     |---|---|---|
     | Pickup, delivery (done) | yes | yes |
     | Pickup, delivery (done), trailer drop (open / done) | yes | no / yes |
     | Pickup, delivery (done), driver-only stop | yes | yes |
     | Multi-drop, last open / delivered | no / yes | no / yes |
     | Delivery by hand, pickup open | no | no |
     | Delivery by hand, pickup done, later drop open | yes | no |
     | Final delivery overridden not done / done | no / yes | no / yes |
     | "No truck" carried to a stop without a truck | yes | yes |
     | Legs all completed, source stops open | yes | yes |
     | Source closed and delivered, a leg open | yes | no |

     Existing data, counted read-only in production on 2026-09-27: no
     load not in execution has a recorded delivery with a later attended
     stop open (so none re-enters a truck's itinerary); two loads in
     execution, 1403 and 1385, are closed at the source with a leg still
     active and leave the Completed tab. They are "source ahead of
     accepted execution" rows (auditor CW2) for recovery through the
     execution owner, not a code change.
   - **4c, implemented (not released).** A prepared summary shows the
     forecast as it stands now: `EtaService.PeekForDisplay`, called when
     the summary is read, answers as `GetCached` does from the same
     keys - the saved plan's identity and version, tracking, stops and
     progress, not display geometry - with no side effects (no demand,
     no refresh wake, no removal, no counters, no scope registration).
     Work per read: no database read, no capture, no preparation;
     overlap is a pure memory read. Measured by tests; not measured in
     production.
   - **4d, implemented (not released).** Commits that change what a
     prepared summary shows reach it after commit, once: base road and
     route commits already did (`PlanningWorkPublication.CommitAsync`); a
     deadhead connection now does too (`DeadheadHistoryPublication.
     CommitAsync`), because the summary's fuel plan is checked against the
     saved connections. A rolled-back publication announces nothing.
     Duty changes are not announced: doing so needs a driver-to-truck read
     on every HOS refresh; the prepared fuel hand-over can lag up to the
     30-second refresh, while the summary's clocks are already read fresh
     at read time. Recorded, not built.
   - **4e ETA display read, implemented (not released).** One owner
     chooses the forecast a summary shows: `EtaForecastService.
     ReadForDisplayAsync`, for every plan of a board, map or preview read
     at once. The store is the truth; a process' memory holds only
     forecasts it committed and is a faster copy. Both are judged by the
     one decision (`EtaService.Decide`) against the plan's work and road,
     and the later calculation of the same work wins in either direction;
     of two calculated at the same instant the saved one wins, because
     the store kept the first committed and refused the other, whose
     process may still hold it. A forecast for other work is never
     chosen, however new.
     - The root's row now holds the whole chain's forecast, as memory does
       and the map shows (pending later loads included), with SHA-256
       hashes of the work and road keys of the plan it was calculated on
       (`DispatchEtaForecasts.WorkKey`, `RouteKey`, migration
       `RecordEtaForecastWork`, not applied anywhere). Followers' rows
       stay filtered and keyless. The board takes each load's part when
       it reads (`PopulateAsync`), which is what it got before.
     - Saved forecasts are read through the read cache, one statement per
       batch of scopes, keyed by the generation the load started under:
       a warm read costs no query, and a load that raced an invalidation
       is stored under the old generation and never read again. A commit
       invalidates its scopes after commit, locally and, through
       `CacheInvalidationRelay`, on other processes; until the relay
       delivers it another process keeps showing the older committed
       forecast - honestly older, never an uncommitted one.
     - Old rows: saved before the keys, they cannot be judged against a
       plan and are never shown by this read; a process without its own
       copy shows none until the next committed refresh of that truck
       writes the keys (how soon after release is not measured). Their
       filtered forecast still serves the board.
     - Tests (two processes on one database, each with its own memory,
       read cache and relay): cold read of the other's commit in one
       query, warm in none; a warm reader moves only through the relay; a
       load across a commit and relay is not kept; an older calculation
       refused by the store is neither remembered nor shown; a tie shows
       the stored one on both; keyless rows are not shown until
       recommitted; the board's part of the chain matches the old row
       shape; the keys' upsert on SQLite and PostgreSQL.
     - Producer to consumer: with a driver's fresh hours and a fresh
       position on the road, a real refresh calculates the chain with
       stops of both loads, commits it, and another process shows the
       same forecast from one query; the board shows the root load its
       part. Rows written by hand in the other tests prove selection and
       filtering only.
   - **4e fuel callers, measured and shared (not released).**
     `TruckFuelPlans.ApplyAsync` runs for the summary's preparation and
     its publisher (itinerary and hours supplied), before the price
     refresh (neither) and in the fleet loop (neither). Measured on the
     SQLite fixture (`FuelCallerCostTests`): 8, 6, 10 and 6 statements;
     the same 6-statement check of the saved plan's roads and history
     (`FuelSavedInputsValidation`) ran in each. The price refresh that
     follows costs 7: a fresh read of the saved plan from the store (it
     decides whether to recalculate, a justified fresh read) and the
     same check. One refresh checked it four times. The
     final inputs differ; the check's inputs - saved plan, remaining
     roads, selected loads - do not.
     - The owner now shares the check within an operation that declares
       itself one unit (`Share`, held by `PlanningRefreshOperation` for a
       refresh). The answer is reused for the same inputs of the check -
       remaining roads, the history batches it replays with their
       signatures, and the selected loads (a saved plan's calculation time
       is not an identity) - while the truck's `planning-inputs` item and the
       new `fuel-saved-inputs` item keep the generations read before the
       check; route, base road and execution commits bump the first, a
       committed connection (`DeadheadHistoryPublication`) the second,
       locally and through the relay. Outside a share every call checks.
       Measured: preparation 8, publisher 0, refresh 4 (its itinerary
       capture), price refresh 1 - 13 statements instead of 31.
       One share per scope: a nested one is refused, and ending an old
       share never ends a newer one. Tests: unchanged inputs checked once;
       an announced road or connection commit checked again and found; an
       unannounced write found by the next operation; a commit during the
       check not shared; another history signature checked again;
       operations one after another and two open at once in separate
       scopes each check; the refresh operation holds one share for its
       preparation; nesting and late ends.
     - Limits: a write another process has not yet announced is seen by
       the next operation, not this one (tested). The fleet loop is its
       own operation and checks for itself. Equal SQL text in the
       measurement does not prove equal parameters; the repetition is
       established by the code and the share tests. Production cost not
       measured.
   - **4e ETA memory bound, implemented (not released).** `EtaMemory`
     held scopes it was never asked to view - leg identities, published
     summary answers, forecasts - for the process' life, and only a
     process running the ETA worker swept even the viewed ones. Every
     scope is now touched when written or read; `Due` forgets a scope
     idle for ten minutes in every map; and at most
     `EtaMemory.MaximumScopes` (1,024) are held whatever roles the
     process runs - past it the least recently touched go, down to three
     quarters, so trimming sorts once per 256 new scopes. A forgotten
     forecast costs a display read one saved-forecast read. Reported as
     `eta-current` scopes with its limit. Size per scope is not measured.
     A scope's touch and its write, and forgetting it, share one lock per
     scope, and a forget drops every map together, the leg's identity
     included; `Due` and the bound decide from a snapshot and forget only
     if the scope is still idle, or untouched since, under that lock.
   - **4e duty, implemented (not released).** A prepared summary's fuel
     hand-over line (`FuelPlan.IssueState`, each stop's `IssueHorizon`)
     is drawn with the hours read when it was prepared. The reader now
     checks it against the hours read with the summary
     (`FuelIssueWindow.Holds`, one owner of the buffer and freshness): a
     line the hours now draw otherwise is shown as prepared, with
     `StaleDependencies = ["duty"]` and refreshing, and the entry is made
     due (`PlanningSummaryCache.Due`) without taking the ticket of a
     preparation under way. The end time moves by seconds with each
     reading and is not compared; a stale reading is a change (the line
     becomes unknown). Tests: same duty current, other duty stale and
     due, a duty change during a preparation caught by the next read,
     the domain rule. The message says it where the summary's status is
     shown (Dispatch, the map): "Driver duty changed. The fuel hand-over
     is updating." The Client carries the mark in its result and compares
     it, so a result that differs only in it replaces the shown one; a
     server released before the mark sends none, read as none.

The publication checklist for stages 1 to 4e, with the bounded memory
review, is [current-work-candidate-2026-09-27.md][candidate].

[candidate]: ../archive/2026-09/current-work-candidate-2026-09-27.md

Each stage is a separate candidate with its own review; none resets
pending work, forces routing or changes historical stops.

## Bounded work, memory and dependencies

Required of every stage; a stage that cannot show them is not complete.

- **No endless recalculation.** Work is triggered by a committed change to
  a meaningful input, by demand, or by a bounded refresh interval. A
  result that changes nothing a consumer shows does not trigger another
  refresh, and a refresh never re-queues itself for its own publication.
- **Meaningful-input invalidation.** Each cached or prepared result names
  its dependency versions; only a change to one of them invalidates it.
  Generations bumped for unrelated groups do not.
- **Overlap coalescing.** Concurrent requests for the same scoped key
  (company, truck, dispatch, input versions) share one computation.
- **Bounded lifetimes.** Every cache, queue, memo and subscription has a
  size bound, an expiry or an owner that disposes it. Per-truck entries
  are removed when the truck leaves the company's work; client
  subscriptions end with the component or page that opened them.
- **Small owner contracts.** Each fact has one owner behind a narrow
  interface; consumers ask the owner. No screen depends on another
  screen's state, and no "work state service" absorbs unrelated
  calculations. The truck work state is a projection of an existing
  capture, not a new god service.
- **Measurements.** For stages 2 to 4: call counts for cold, warm and
  overlapping reads; background refresh counts per truck per minute; and
  a sustained navigation run (Cards, Papers, Table, Map, Messenger in a
  loop for a fixed period) reporting client heap, server memory and
  request and query counts at start and end. Growth without a bound is a
  failure. These have not been measured yet.

## Commit and invalidation boundaries

The state is derived from committed rows only: the itinerary (execution
and source) and saved route metadata (tracking). Writers that change
either invalidate `planning-inputs` after commit and send
`PlanningSummaryCache.Committed`:

- tracking and route commits through `PlanningWorkPublication.CommitAsync`;
- execution commands and source reconciliation through `MarkTruckDirty`.

Gaps to close: tracking is not in the summary signature (stage 4), and
`Committed` reaches only the local process. `CacheInvalidationRelay`
carries read-cache item invalidations across processes; this is tested
for saved ETA forecasts (stage 4e), not for the summary cache. A late
summary must not replace one built from a newer capture (stage 2 stores
under the capture it was built from).

## Work counts

Code reading, not measured:

- cold summary read: 0 reads (stage 1 test builds the reader without an
  inputs reader or route service);
- warm summary refresh: 3 itinerary captures, and a fuel projection
  replayed in up to four places per truck.

Targets with regression tests: 1 capture per refresh (stage 2); two
overlapping refreshes of one truck coalesce to 1 capture; board, Messenger
and map reads add no capture of their own (stage 3). Foreground and
background cost are reported separately; a fast response is not evidence.

## Auditor rules

Per [consistency-auditor.md](consistency-auditor.md), company-scoped and
bounded, with coverage reported. Implemented as version 1 (not released),
detection only; each rule's limits are part of its contract.

- **CW1 `routing.route-passed-work-open`** (review): work planning has
  passed is not finished - its delivery not recorded, or delivered with
  a later truck stop open. It asks the owner, `WorkPlacements.Conflicts`
  over `TruckPlanningInputs`, for every active truck of the company in
  batches of 100, and decides nothing itself. No grace window: GPS
  passage is not delivery, so it is a review from the moment it shows,
  and its age is the journal's first-seen time. A page reads the whole
  fleet through the owner's cache, not one statement.
- **CW2 `execution.source-closed-work-open`** (review): a load closed at
  its source (`LoadCompletion.ClosedStatus`) whose accepted execution is
  still planned or active - the AMF1395 shape. Source stop actuals ahead
  of the leg are not covered by version 1; the closed status is.
- **CW3 `execution.source-review-open`** (review): a source change
  waiting for a dispatcher (`ExecutionReviewReason`), on a load neither
  closed nor cancelled. The link keeps no time, so the age is the
  journal's first-seen time rather than a threshold.
- **CW4 `routing.summary-names-current-work`** (violation): a stored
  truck summary under the current signature names the owner's current
  work. An entry under an older signature is stale and retired by the
  cache, not reported. Summaries are per process: each process checks
  only its own, and the report says so.

Existing rows found read-only on 2026-09-27 (19:20 UTC), before these
rules run anywhere: CW2 loads 1385 (active leg), 1395 (planned) and 1403
(active); CW3 load 1416 (initial assignment review). CW1 and CW4 need
the running API. Recovery stays with Root and the dispatcher; the rules
change nothing.

## Existing data

The stages add derived state only; nothing is stored, so there is no
backfill. Existing invalid rows are execution rows, and their recovery is
a reviewed reconciliation, never an automatic change from GPS or source:

- AMF1395-shaped legs: found read-only by CW2; recovery is owned by Root
  (incident) and done per load.
- Legacy loads with a stored planning revision: none open on 2026-09-27.
- Summaries, previews and ETA memory are per process and expire; a
  release needs no cache migration.
- Saved ETA forecasts without keys (every row before stage 4e): not
  shown by the display read until the truck's next committed refresh
  writes them; no backfill, since keys can only come from the plan the
  forecast was calculated on.

## Rules for future features

- Read the truck's current work from `TruckPlanningInputs` (`CurrentWork`,
  `CurrentSegment`, later the work state). Do not take the first
  candidate, write a new selection loop, or derive it in the client.
- A new fact about work (tolls, driver pay, owner-operator settlements)
  consumes this state and names its dependency versions.
- Architecture tests to add with stage 2: selection calls to
  `PlanningWorkPolicy.IsCompleted` and `Candidates(...).FirstOrDefault()`
  only in the owner and an explicit, shrinking allowlist. With stage 3:
  no status- or date-based current logic in Client pages.
