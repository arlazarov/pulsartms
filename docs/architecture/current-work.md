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
4. **Completion and invalidation.** One completion owner with membership
   rules expressed through it; tracking version in the summary signature;
   ETA, deadhead, base road and duty commits notify the summary after
   commit; one ETA reader; remove duplicate fuel replays.

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
`Committed` reaches only the local process; whether
`CacheInvalidationRelay` carries item invalidations across processes is
not verified. A late summary must not replace one built from a newer
capture (stage 2 stores under the capture it was built from).

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
bounded, with coverage reported. None is implemented; each stays a
recorded gap until it is.

- **CW1 route-passed-not-delivered:** tracking passed all stops for the
  accepted assignment and no delivery actual after a threshold.
- **CW2 source-ahead-of-accepted:** source actuals or status beyond the
  accepted leg (the AMF1395 shape).
- **CW3 unresolved source review:** an open review older than a
  threshold.
- **CW4 consumer agreement:** a stored summary's dispatch, leg or
  revision differs from a fresh capture's current work beyond the
  freshness window.

## Existing data

The stages add derived state only; nothing is stored, so there is no
backfill. Existing invalid rows are execution rows, and their recovery is
a reviewed reconciliation, never an automatic change from GPS or source:

- AMF1395-shaped legs: found read-only by CW2; recovery is owned by Root
  (incident) and done per load.
- Legacy loads with a stored planning revision: none open on 2026-09-27.
- Summaries, previews and ETA memory are per process and expire; a
  release needs no cache migration.

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
