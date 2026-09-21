using System.Text.Json;
using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Interfaces;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Eta;
using Domain.Rules.Ports;
using Microsoft.Extensions.Options;

namespace Application.Features.Eta.Services;

public sealed class EtaService(
  IAppDbContext db,
  IDriverHosProvider hos,
  IRouteRegionLookup regions,
  EtaMemory memory,
  IHosHistoryProvider historyProvider,
  IOptions<EtaPlanningOptions> planningOptions
)
{
  private readonly EtaPlanningOptions planning = planningOptions.Value;

  private static string RouteKey(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.Id,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Version,
        state.Plan?.Tracking.NextStopId,
        state.Plan?.Tracking.PassedStopIds,
        state.Plan?.InputsChanged,
        state.Plan?.Stops,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
      }
    );

  public DispatchEta? GetCached(RoutePlanningState state)
  {
    if (state.Plan is not { } plan)
      return null;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    memory.View(key, DateTime.UtcNow);
    if (memory.Results.TryGetValue(key, out var entry))
    {
      if (
        entry.RouteKey == RouteKey(state)
        && entry.Value.ValidUntil > DateTime.UtcNow
      )
        return entry.Value;
      if (memory.RemoveIfCurrent(key, entry))
        memory.RequestRefresh();
    }
    return null;
  }

  public async Task<DispatchEta?> GetAsync(
    RoutePlanningState state,
    CancellationToken ct,
    bool viewed = true,
    EtaChainPlan? chain = null
  )
  {
    if (state.Plan is not { } plan)
      return null;
    var now = DateTime.UtcNow;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    if (viewed)
      memory.View(key, now);
    var driver = plan.ExecutionLegId is { } legId
      ? await db
        .ExecutionLegs.AsNoTracking()
        .Where(x =>
          x.Id == legId
          && x.TruckId == plan.TruckId
          && x.Revision == plan.AssignmentRevision
          && (x.Status == "active" || x.Status == "planned")
        )
        .Select(x =>
          x.DriverId.HasValue
            ? db
              .Drivers.Where(d => d.Id == x.DriverId)
              .Select(d => d.ExternalId)
              .FirstOrDefault()
            : null
        )
        .SingleOrDefaultAsync(ct) ?? ""
      : await db
        .Trucks.AsNoTracking()
        .Where(x => x.Id == plan.TruckId)
        .Select(x => x.Driver == null ? "" : x.Driver.ExternalId)
        .SingleOrDefaultAsync(ct) ?? "";
    var signature = JsonSerializer.Serialize(
      new
      {
        plan.Id,
        plan.ExecutionLegId,
        plan.AssignmentRevision,
        plan.Version,
        driver,
        plan.Stops,
        plan.Tracking.NextStopId,
        plan.Tracking.PassedStopIds,
        plan.InputsChanged,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
        Chain = chain?.InputHash,
      }
    );
    var gate = memory.Gate(key);
    await gate.WaitAsync(ct);
    try
    {
      if (
        memory.Results.TryGetValue(key, out var cached)
        && cached.Signature == signature
        && cached.Value.ValidUntil > DateTime.UtcNow
      )
        return cached.Value;
      var clocks = await hos.GetClocksAsync(ct);
      clocks.TryGetValue(driver, out var clock);
      var history = clock is null
        ? null
        : await historyProvider.GetAsync(driver, ct);
      var result = Calculate(state, clock, DateTime.UtcNow, history, chain, ct);
      if (clock is null)
        result = result with { ValidUntil = DateTime.UtcNow.AddSeconds(10) };
      ct.ThrowIfCancellationRequested();
      memory.Results[key] = new(signature, result, RouteKey(state))
      {
        ChainInputHash = chain?.InputHash,
      };
      return result;
    }
    finally
    {
      gate.Release();
    }
  }

  public DispatchEta Calculate(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    EtaChainPlan? chain = null,
    CancellationToken cancellationToken = default
  )
  {
    var baseline = CalculatePlan(
      state,
      clocks,
      now,
      history,
      chain,
      HosCycleMode.Observe,
      cancellationToken
    );
    if (
      !baseline.Stops.Any(stop =>
        stop.Hours is { CycleVerified: true, FirstCycleShortageAt: not null }
      )
    )
      return baseline;
    DispatchEta? Alternative(HosCycleMode mode)
    {
      try
      {
        return CalculatePlan(
          state,
          clocks,
          now,
          history,
          chain,
          mode,
          cancellationToken
        );
      }
      catch (CycleScenarioUnavailableException)
      {
        return null;
      }
    }
    var recap = Alternative(HosCycleMode.Recap)
      ?.Stops.ToDictionary(stop => (stop.DispatchId, stop.StopId));
    var restart = baseline.Stops.Any(stop =>
      stop.Hours?.FirstCycleShortageAt is not null && stop.Appointment >= now
    )
      ? Alternative(HosCycleMode.Restart)
        ?.Stops.ToDictionary(stop => (stop.DispatchId, stop.StopId))
      : null;
    return baseline with
    {
      Stops = baseline
        .Stops.Select(stop =>
        {
          if (
            stop.Hours
            is not { CycleVerified: true, FirstCycleShortageAt: not null } hours
          )
            return stop;
          var alternatives = new List<StopHoursAlternative>();
          void Add(
            IReadOnlyDictionary<(Guid, Guid), StopEta>? candidates,
            bool requireOnTime
          )
          {
            var candidate = candidates?.GetValueOrDefault(
              (stop.DispatchId, stop.StopId)
            );
            if (
              candidate?.Hours
                is not {
                  CycleVerified: true,
                  FirstCycleShortageAt: null
                } candidateHours
              || requireOnTime && candidate.LateMinutes != 0
            )
              return;
            alternatives.AddRange(candidateHours.Alternatives);
          }
          Add(recap, false);
          Add(restart, true);
          return stop with
          {
            Hours = hours with
            {
              Alternatives = alternatives,
              UnavailableReason =
                alternatives.Count == 0
                  ? "A verified cycle alternative is not available within this forecast horizon."
                  : null,
            },
          };
        })
        .ToArray(),
    };
  }

  public DispatchEta CalculateRoad(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    EtaChainPlan? chain = null,
    CancellationToken cancellationToken = default
  ) =>
    CalculatePlan(
      state,
      clocks,
      now,
      history,
      chain,
      HosCycleMode.Observe,
      cancellationToken
    );

  public DispatchEta CalculateRoadPreview(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    CancellationToken cancellationToken = default
  ) =>
    CalculatePlan(
      state,
      clocks,
      now,
      history,
      null,
      HosCycleMode.Observe,
      cancellationToken,
      boundedPreview: true
    );

  private DispatchEta CalculatePlan(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history,
    EtaChainPlan? chain,
    HosCycleMode cycleMode,
    CancellationToken cancellationToken,
    bool boundedPreview = false
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    var currentRegion =
      state.Progress is { LocationStale: false, Position: { } currentPosition }
      && double.IsFinite(currentPosition.Latitude)
      && currentPosition.Latitude is >= -90 and <= 90
      && double.IsFinite(currentPosition.Longitude)
      && currentPosition.Longitude is >= -180 and <= 180
        ? regions.Find(currentPosition)
        : null;
    var dutyStatus = HosDutyStatus.Read(
      history,
      clocks,
      now,
      currentRegion is { NorthOf60: false } ? currentRegion.Country : null
    );
    DispatchEta Missing(string reason, bool routeUpdatePending = false) =>
      new(now, now.AddMinutes(2), [], reason, [])
      {
        DutyStatus = dutyStatus,
        RouteUpdatePending = routeUpdatePending,
      };
    if (EtaReadiness.Reason(state, clocks, chain, planning, now) is { } wait)
      return Missing(wait.Reason, wait.RouteUpdatePending);
    var plan = state.Plan!;
    var progress = state.Progress!.ProgressMiles!.Value;
    if (plan.Route.Legs.Count == 0)
      return Missing("ETA unavailable: no route legs.");
    var timing = boundedPreview
      ? EtaRouteTiming.CompilePreview(plan.Route, regions, cancellationToken)
      : memory.Timing.GetOrCreate(plan.Id, plan.Version, plan.Route, regions);
    if (timing is null)
      return Missing(
        "Schedule preview regional timing exceeds its bounds or needs verification."
      );
    if (!timing.HasCompleteTravelTimes)
      return Missing("ETA unavailable: incomplete road travel times.");
    var initial =
      currentRegion
      ?? regions.Find(state.Progress?.Position ?? plan.Route.Legs[0].Points[0]);
    if (initial.Country == "" || initial.NorthOf60)
      return Missing(
        "ETA unavailable: this regional ruleset needs verification."
      );
    // EtaReadiness has already answered for missing hours: a forecast
    // without them never reaches this line.
    var clock = new HosTravelClock(
      now,
      clocks!,
      initial.Country,
      history,
      planning,
      cycleMode,
      EtaAssumptions.FuelStopsAhead(plan.FuelPlan)
    );
    var cycleAtCalculation = clock.SnapshotCycle();
    try
    {
      clock.CompleteOngoingDailyRest(dutyStatus);
      if (state.Progress?.OffRoute == true)
        clock.Drive(
          planning.TravelHours(
            state.Progress.DistanceFromRouteMiles
              * planning.OffRouteDistanceFactor,
            0
          ),
          initial.Country
        );
    }
    catch (CycleScenarioUnavailableException error)
    {
      return Missing(error.Message);
    }
    var assumptions = EtaAssumptions.Opening(clock, planning);
    var walk = new EtaWalk(
      clock,
      regions,
      planning,
      state,
      plan,
      chain,
      now,
      cycleMode,
      cancellationToken
    );
    walk.Run(timing, progress);
    var results = walk.Results;
    var pending = walk.Pending;
    var blocked = walk.Blocked;
    EtaAssumptions.Closing(
      assumptions,
      clock,
      state.Progress?.OffRoute == true
    );
    return new(
      now,
      now.AddMinutes(2),
      results,
      results.Count == 0 ? blocked : null,
      assumptions
    )
    {
      DutyStatus = dutyStatus,
      CycleAtCalculation = cycleAtCalculation,
      PendingDispatches = pending,
    };
  }
}
