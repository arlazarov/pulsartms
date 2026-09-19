# Planning cohesion review — September 18, 2026

## Scope and evidence

This is a source review of the recent Route, ETA, fuel and Fleet Map changes.
It is not a line-by-line audit of the entire repository. The working tree
contains a much larger accumulated rebuild, so its total diff cannot measure
how much debt the recent fixes introduced. No application code or database
state was changed for this review.

The preceding implementation run passed 2,784 Server tests, 1,032 Client C#
tests and 569 JavaScript tests, plus a strict solution build. Those results
verify the tested behavior, not architectural cohesion. This review did not
repeat tests, benchmark performance, or validate live route/ETA accuracy.

## Assessment

The execution foundation remains useful: accepted stop identities, assignment
revisions, immutable calculation inputs, publication guards and provider
interfaces already exist. The weaker area is the orchestration and display
contract between Execution, Routing, ETA and Fuel. Recent fixes increasingly
add special cases at that boundary instead of completing the shared model.

Engineering judgment: planning cohesion is approximately 6/10. This is a
qualitative assessment, not a measured completion percentage. A focused
consolidation of these boundaries is warranted before extending accounting,
tolls or settlements through the current planning response models.

## Findings

### 1. Expected fuel outcomes travel through exceptions after commit

`FuelPlanningService.Access.cs:84` commits a saved station warning, then throws
`RoutePlanningException` with `FuelAccessProblemSaved = true`.
`AutomaticPlanningService.cs:257` catches that flag and turns the operation back
into a normal planning response. The direct `BuildFuelPlanHandler` does not
perform that conversion.

This makes a valid product outcome look like a failure to some callers after
the operation has already written data. The new path also revives the old
`FuelRecommendations` storage for a different meaning: an access problem.

Replace the control flow with a typed calculation result. Separate a feasible
fuel plan, a reachable candidate below reserve, an unreachable candidate with
shortfall, missing inputs, and an actual technical failure. A candidate can be
reachable even when no complete itinerary plan is feasible. None of these
states should require a caller to interpret an exception flag or message text.

### 2. Display reconstruction can fail the operational route calculation

`RouteDisplayReference.ReconnectAsync` performs an additional provider call to
reconstruct the last passed stop to the GPS origin. Provider failure or failed
anchoring propagates through route build, automatic rerouting or route-choice
saving before publication of the valid remaining route.

Separate operational road publication from optional historical display context.
A failed reconstruction should retain identified previous context or report
context unavailable; it should not discard an otherwise valid remaining route.
Record reconstructed context as estimated road, distinct from GPS observations.
Keep its publication tied to the route version it describes.

### 3. Current driver selection remains inconsistent across consumers

`EtaService.cs:73` selects the exact plan execution leg and accepts `active` or
`planned` status. `TruckPlanningInputsReader.cs:147` selects the first active
native segment, otherwise the truck catalog driver. The latter selection does
not use the same saved-completion/current-work decision as the ETA root.

These paths can select different drivers when an older active segment is
already complete by route evidence and a planned segment is the current work.
This is a source-level inconsistency; this review did not reproduce a specific
live driver mismatch. The recent 11006 status fix corrected one consumer but
not the ownership of the rule.

Extend the existing immutable work context with one resolved current identity,
effective crew and eligibility result. Derive it after shared current-work
selection. HOS display, ETA, fuel schedule evaluation and route choices should
consume it rather than run separate status-based driver queries.

### 4. Map code interprets cargo state

`emptyRouteLegs.js` decides that an approach to an initial pickup is empty and
interprets `Empty`/`Bobtail` strings. `routeLayer.js` overlays separate
empty segments on current/traveled geometry. This fixed the visual symptom,
but the Client owns part of the movement classification.

Publish explicit segment purpose and cargo state from accepted execution facts.
Represent unknown load state explicitly. Render the same segments in Fleet Map
and Dispatch. The Client should select styles, not decide whether the truck is
empty from a pickup label or a positional offset.

### 5. Fuel warning validity is weaker than fuel-plan validity

`ReportFuelAccessAsync` stores a formatted deficit and checks the current road
and itinerary before publication. The warning selection used the fuel horizon,
but this path does not carry the full road/history dependency set used by
`CommitAsync` for a feasible fuel plan. It also derives quantities from
`state.Profile`, while the search can use a requested profile.

The read path in `RoutePlanningService.cs:120` expires the warning by route
version, input status and five-minute age. It does not bind the warning text to
a fuel observation identity. `ProjectRecommendations` can update distance while
leaving the saved deficit text unchanged. A new fuel reading can therefore make
the warning stale before its age limit.

Give all planning outcomes an explicit input stamp, including work revision,
road dependencies, effective settings and applicable telemetry. Keep shortfall
and reachability as structured server values with an observation time. Format
messages at the presentation boundary. Refresh/reproject according to those
inputs, rather than relying on a blanket timeout.

### 6. Reserve preferences and physical feasibility need a clearer policy

`FuelReservePolicy.FirstArrivalMinimum` now always returns zero but still takes
unused starting fuel and profile arguments. Later purchases retain the normal
reserve constraint. `FuelRegionPlanner` separately enforces a half-tank arrival
floor in poor or expensive regions. These are materially different constraints.

Define explicit policy terms: physical reachability, preferred reserve,
post-purchase reserve, terminal arrival requirement and economic target.
An ordinary below-reserve warning is not a physical impossibility. A regional
arrival preference should not be reported as inability to reach the first
station. Keep the user's accepted behavior while making these distinctions
visible in calculation results and tests.

## Consolidation sequence

1. Replace exception-based fuel access publication with typed outcomes. Preserve
   old feasible plans on technical failure; expose diagnostic candidates without
   financial totals or a claim that the trip is feasible. Keep both API entry
   points consistent and give publication the full captured dependency set.
2. Consolidate current work, effective driver/crew and eligibility in the
   existing immutable input pipeline. Remove duplicate consumer decisions.
3. Separate operational remaining routes from versioned display context. Add
   explicit segment kinds, cargo state and provenance; remove Client inference
   and prevent context failure from blocking operational route publication.
4. Centralize result freshness and fuel constraint semantics. Tie warnings to
   telemetry and settings; format structured outcomes in the Client. Remove
   superseded compatibility branches instead of adding a new permanent layer.

Keep execution identities, accepted revisions, provider interfaces and existing
publication protections. Do not introduce another parallel execution model or
a universal service that absorbs every feature. Assess any storage migration
only after finalizing the result/context contract; no reset is justified by
this review alone.

## Acceptance scenarios

- A planned current leg and an older active-but-complete leg resolve the same
  effective driver for HOS display, ETA and fuel scheduling.
- A station reachable below reserve remains selectable and carries a warning.
- An unreachable station carries a numeric deficit and no feasible-plan totals.
- Refueling invalidates or reprojects the prior warning without waiting five
  minutes; route/profile/work changes cannot retain a stale diagnosis.
- Failure of reconstructed historical geometry does not discard a valid new
  remaining route or its independent calculations.
- Reconstructed road is never presented or consumed as recorded GPS mileage.
- Fleet Map and Dispatch render identical server-defined segment meanings.
- Manual, automatic and alternative-route flows publish the same outcome types
  and reject stale results after assignment, telemetry or road changes.
- Accounting imports and future settlements consume explicit facts/estimates,
  not map geometry or fuel-warning strings.
