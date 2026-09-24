# Reroute policy: diagnosis and change (September 24)

Local only. Nothing was deployed; no provider was called.

## How a departure became a new road before

- **GPS.** The telemetry feed and the high-frequency location stream are
  each polled every `TelemetrySeconds` (60). One poll publishes the fixes
  since the last one.
- **Judgement.** The planning loop runs every `PlanningSeconds` (30) plus
  its own duration, at most `MaxTrucksPerPlanningCycle` trucks, one after
  another. For each truck the tracking pass fed every new fix to stop
  tracking and movement, but `RerouteDecision` saw only the newest fix.
- **Persistence.** The truck had to be more than 2 miles off. The clock
  started at the newest fix of the first pass that saw it off, not at the
  first fix that was off. It then needed a later newest fix 180 seconds
  after that.
- **Cooldown.** Five minutes, plus a one-mile move since the last new road.
- **Budget.** `RouteRecalculationBudget` is disabled in production by
  operator request. Only the cooldown, the one-mile move and the shared
  TomTom limits bound reroute calls per truck.

For a truck turning straight away from its road at 60 mph, that gives two
minutes to reach 2 miles, up to about a minute to be judged, three minutes
of persistence, and up to another minute and a half for a later fix and
pass. Fixes either side of the threshold reset the clock on every pass.

## Change

`RerouteDecision.Judge` now receives the new, unique fixes since the last
pass, oldest first, and judges each at the pass's own time.

- **Two fixes, the persistence time apart** (`RouteDeviationSeconds`, now
  60) confirm a departure.
- **Two fixes each twice the threshold away** confirm it with no wait. One
  far fix alongside a moderate one does not.
- **One fix never does**, however far off.
- **Stale fixes** neither confirm nor clear.
- **Hysteresis.** The truck is back on the road only inside 60% of the
  threshold.
- **The threshold** stays 2 miles. Providers report no position accuracy,
  so it is kept far above GPS error.
- **Cooldown.** `RerouteCooldownSeconds` is 150. A confirmed departure waits
  for it; it is not dropped.
- **Unchanged:**
  - the one-mile move;
  - the deadhead-to-pickup and dispatcher-request paths;
  - the stop-radius rule;
  - publication guards and the provider error memory.

The tracking state gains `OffRouteFixes`, `OffRouteFarFixes` and
`OffRouteConfirmedAt`. They are written only when set, so plans without a
departure serialize as before. Counts are capped at two, so a truck that
stays off rewrites its plan once per step, not per fix. A plan saved
before this change with only `OffRouteSince` starts counting at its next
fix. No migration.

## Measured

`RerouteReplayTests` replays fixes every 10 seconds, published every 60 and
judged every 30. It uses a fixed clock. It counts **verdicts**, not
provider calls or production latency. Before the change the same replays
gave:

| Replay | Before: first verdict | After |
| --- | ---: | ---: |
| Turn away at 60 s | 360 s | 240 s |
| Road 2.1 mi off, ±0.25 jitter | 240 s | 120 s |
| Stays 3 mi off, road never changes | 1 verdict in 9 min | 3 (120, 270, 420 s) |
| Jitter ±0.15, single-fix outliers | none | none |

The last row is the cost side. A truck that stays off a road the provider
keeps returning gets one new road per 150 s instead of per five minutes.

`AutomaticPlanningRerouteTests` goes through the real owner and counts
provider calls:
- One departure reroute asks the provider twice: the new road, and the
  reconnect of the displayed road.
- A departure confirmed inside the cooldown is kept and costs exactly one
  reroute once the cooldown has passed.
- A provider failure leaves the saved plan and its departure as they were.
  A repeat within the error memory makes no call, and the same fixes then
  confirm the departure once.
- A fix judged at one pass is not counted again at the next.
- A work change during the provider call discards the answer.

Mutation checks:
- Restoring "any two off fixes and a far current one" fails the
  moderate-plus-outlier replay.
- Re-feeding an already judged fix fails the dedup test.

## Not done, and what would do it

- **Detection is still paced by the planning pass**, not by telemetry
  arrival. Where the rest of the delay comes from:
  - fixes reach the server once per `TelemetrySeconds` (60);
  - the planning pass (every 30 s plus its run time) is not aligned with
    that poll, so it adds 0–30 s, about 15 s on average;
  - with four trucks, every truck is judged on every pass
    (`MaxTrucksPerPlanningCycle` is 10).

  A cheap check when fixes are published, requesting that truck's planning
  through the existing queue, would save only that pass delay. It needs its
  own coalescing per truck and assignment and a controlled-interleaving
  test against the pass. Not started: the gain is about 15 s at this scale.
  The larger lever is `TelemetrySeconds` itself, an owner decision because
  it raises Samsara request volume.
- **Fuel stops already sent.** Read from the code, not tested.
  - *What changes:* a reroute marks the fuel plan for refresh, as before.
    Once the truck is back on a road with a fresh position,
    `FuelPriceRefreshService` recalculates the plan automatically. The
    recalculation does not pin stops already given to the driver, so a
    reroute, even a small one, can change the plan's stations.
  - *What does not:* what the driver was given. A hand-over is written only
    on an explicit confirmation (`FuelIssueRecords`), automatic sending is
    off, and the display marks a stop whose plan no longer says what was
    sent. Nothing is re-sent on its own.
  - *Decision for the owner:* whether a small reroute should keep sent stops
    pinned. It is not built. The shorter cooldown makes reroutes, and so
    recalculations, somewhat more frequent for a truck that stays off its
    road.
- **Real latency and provider volume** need the telemetry cadence of real
  trucks. Neither was measured; the load fixture's detour scenario was not
  rerun.
- **ETA** is untouched and stays a separate routine.
- **Consistency auditor:** not applicable. The departure state is derived
  from GPS and rewritten at every pass; no durable row can be invalid on its
  own. The regressions above cover the invariant.
