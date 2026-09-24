# Source structure review, 2026-09-23

Scope: a report on existing maintained code under the reviewed-exception size
policy (since replaced by [cohesion review](../../architecture/cohesion-review.md)). No code was changed
for this report; fixes are separate, reviewable commits after it. Size alone is
not treated as evidence of a problem. The inventory was read from the tree at
`4d468d4a`; line numbers may have moved since.

## Summary

No server file exceeds 400 lines and the reviewed-exception registry is empty,
after the splitting done between 2026-09-20 and 2026-09-21. Most of those
splits follow responsibilities. What the review found instead is duplicated
logic between files, a shared vocabulary owner most code does not use, two
residue files that exist to satisfy the old limit, and one method pair that
duplicates a whole pipeline. Client Razor and C# have no line rule; the largest
page is cohesive enough by partials but carries display maths in its main file.

## Findings

Priority: P1 correctness risk, P2 drift or duplication likely to cause one,
P3 readability.

| # | Where | Finding | Proposed owner boundary | Benefit | Risk | Size |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `FuelPlanningService.cs` `BuildCoreAsync` and `FuelPlanningService.Editing.cs` `EditCoreAsync` | The same pipeline (gate, load capture, prices, final-stop route plan, schedule setup, roads merge) is written twice, about 300 lines each | A private inputs step both paths call | Removes about 80 duplicated lines and future drift | Medium: cache flags differ between the paths | Larger; staged |
| 2 | `FuelPlanningService.Editing.cs` fuel-gauge check and `Domain/Rules/Routing/FuelStartingLevel.cs` | The editor re-implements the starting-level rule without the reserve error and with a different message (P2) | `FuelStartingLevel` | One rule | Changes preview behaviour below reserve: product decision first | Small, after the decision |
| 3 | `Domain/Rules/SourceWords.cs`, used by five files; inline job and status words in about 13 and 20 files | The shared vocabulary owner exists, most checks do not use it. `DispatchWorkspaceRules` accepts only "Pick Up" for new stops but "Pick Up" or "Pickup" in its ordering check: consistent today because the editor sends canonical words, fragile for imported stops (P2) | `SourceWords` | One reading of words that already caused driver-visible bugs | Case-insensitive matching changes validation of typed input | Small per feature |
| 4 | `DispatchWorkspaceReader.Review.cs` (34 lines) and `DispatchWorkspaceReader.cs` `ReadAsync` (340 lines) | A residue partial made for the old limit holds two helpers, one equal to `SourceWords.MovesCargo`; the real weight, nine reads and a stop projection in one method, stayed | Fold the helpers back; extract a stop projector (as done for `ExecutionWorkReader`) | Readable reads | Low | Small |
| 5 | `RouteChoiceService.cs` (13 injected services) and `RouteChoiceService.Publication.cs` (13 lines) | A pass-through `planning.ProfileAsync` beside an injected `profiles`; an 8-line invalidation helper split off for size | Call `profiles` directly; fold the helper back; later a persistence class for the save writes | One cross-service dependency fewer | Low for the first two | Small, then larger |
| 6 | 39 `reads.Invalidate("...")` calls; one four-key sequence repeated in five commands | Cache region names are plain strings at each call site | Named regions on `ReadCache` | A typo cannot skip an invalidation silently | Low | Small |
| 7 | Five copies of a `Mark`/`Take` timing helper around `PerformanceStages.Elapsed` | Identical helpers | `PerformanceStages` | Less noise | Trivial | Small |
| 8 | `GoogleAddressGeocoder.cs` (393) | HTTP, cache and gate beside address matching whose internal statics `GoogleAddressValidation` borrows | A `GoogleAddressMatching` static class | Matching testable alone; geocoder about 170 lines | Low | Small |
| 9 | `GetNextLoadRoutes.cs` (374) | One handler sends another query and rebuilds the "source loads minus execution-owned loads" merge found in three other places (P2) | The merged work read from `ExecutionWorkReader` or `TruckItineraryReader` | One source of truth | Medium: fleet-efficiency read path | Larger |
| 10 | `CreateDispatch.cs`, `UpdateDispatchWorkspace.cs`, `CorrectDispatchStop.cs` | The same idempotency-receipt replay copied three times | `DispatchWorkspaceReceipts.ReplayAsync` | One replay rule | Low | Small |
| 11 | Seven components with hand-written "latest request wins" guards (for example `FuelPlanEditor.razor.cs`, `DispatchDetails.razor.cs`) | Each checks a slightly different identity (P2) | A small shared latest-request helper | Fewer late-result races | Behaviour at each site | Larger |
| 12 | `Scripts/fleetMap/stations/stationLayer.ts` (at its shrink-only budget) | No headroom: the next change forces a move under time pressure | A planned responsibility-based extraction | Avoids a rushed split | Low | Small |

## Large but fine

- `TomTomRoutingProvider` (five files): request gates and limits are shared
  state; each part is named for its concern.
- `FleetSynchronizationOperation` (four files): separate feed loops over one
  snapshot and its locks.
- `AutomaticMileageRecorder.Intervals.cs`: one pipeline from staged intervals
  to recorded mileage or a named gap.
- `TruckItineraryReader.cs`: one read and its projection behind a small
  surface.
- `FuelRouteSearch.cs`, `FuelOptimizer.cs`: pure algorithms without
  dependencies.
- `ConsistencyAuditor.cs`: a coordinator that reaches rules only through
  interfaces.
- `EtaChainInputsService` (three files): describe, prepare and saved roads
  follow responsibilities.
- Client `PlanningDisplayCache.cs` and `FleetMap.razor.cs` partials: one
  bounded cache, and a page split by concern; the main page file's display
  maths is an optional presenter, low priority.

## Proposed order

Small, separate commits: 4, 5 (first two parts), 6, 7, 8, 10, then 3 feature by
feature. Findings 1 and 2 need a product decision on the reserve check first.
9 and 11 are proposals to schedule, not to do beside feature work. None of
these is started by this report.
