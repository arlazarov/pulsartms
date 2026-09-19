using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Queries;
using Microsoft.Extensions.Caching.Memory;
using Application.Caching;

namespace Application.Features.Routing.Services.Routes;

public sealed class AutomaticPlanningService(RoutePlanningService plans, FuelPlanningService fuel,
  Application.Features.Dispatch.Services.DispatchBoardService dispatchBoard, IMemoryCache cache, PlanningReadService planningReads, ProcessGates processGates)
{
  private readonly KeyedGates gates = processGates.For<AutomaticPlanningService>();

  public async Task<AutomaticPlanningResult> ForTruckAsync(Guid truckId, CancellationToken ct)
  {
    var board = await dispatchBoard.ReadAsync(new(TruckId: truckId, IncludeHos: true, IncludeFinancials: false), ct);
    var loads = board.Items.FirstOrDefault()?.Dispatches;
    foreach (var load in loads ?? [])
    {
      var resolved = await plans.LoadAsync(load.Id, ct);
      if (resolved.TruckId != truckId)
        return new(truckId, load.Id, load.LoadNumber, null, "This load has multiple truck assignments; its route is not available for this truck.");
      var result = await ForDispatchAsync(load.Id, ct,
        connectFromTruck: load.Status.Equals("assigned", StringComparison.OrdinalIgnoreCase));
      if (result.State?.Plan is not { Tracking.AllStopsPassed: true, InputsChanged: false })
        return result with { Hos = board.Items.FirstOrDefault()?.Hos };
    }
    return new(truckId, null, null, null, "No remaining stops in current or upcoming dispatches.") { Hos = board.Items.FirstOrDefault()?.Hos };
  }

  public async Task<AutomaticPlanningResult> ForDispatchAsync(Guid dispatchId, CancellationToken ct, bool connectFromTruck = false)
  {
    var gate = gates.For((await plans.LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      var load = await plans.LoadAsync(dispatchId, ct);
      var state = await plans.GetAsync(load, ct);
      var key = $"automatic-planning-error:{dispatchId}:{PlanningSettingsService.Signature(state.Profile)}";
      if (cache.TryGetValue<string>(key, out var previousError))
        return Result(state, previousError);
      try
      {
        var built = false;
        if (state.Plan is null || state.Plan.InputsChanged)
        {
          await plans.BuildAsync(dispatchId, new(state.Profile), ct);
          built = true;
        }
        var advanced = await plans.AdvanceAutomaticallyAsync(dispatchId, ct,
          forceReroute: connectFromTruck && state.Plan is { FromCurrentPosition: false });
        if (built || advanced) state = await plans.GetAsync(dispatchId, ct);
        var plan = state.Plan!;
        if (plan.Tracking.AllStopsPassed) return Result(state, "All dispatch stops passed.");
        // Fuel purchases are calculated explicitly by the dispatcher. Background
        // route tracking may invalidate suggestions but must not replace them.
        return Result(state, null);
      }
      catch (Exception ex) when (ex is RoutePlanningException or HttpRequestException)
      {
        var message = ex is RoutePlanningException ? ex.Message : "Route service is temporarily unavailable. Retrying automatically.";
        cache.Set(key, message, TimeSpan.FromMinutes(2));
        return Result(state, message);
      }

      AutomaticPlanningResult Result(RoutePlanningState value, string? message)
      {
        ProjectRecommendations(value);
        return new(load.TruckId!.Value, load.Id, load.LoadNumber, value, message);
      }
    }
    finally { gate.Release(); }
  }

  public async Task<AutomaticPlanningResult> RecalculateFuelAsync(Guid dispatchId, CancellationToken ct)
  {
    var gate = gates.For((await plans.LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      // Replacement is committed only after validation; reads independently reject invalid older plans.
      var state = await plans.GetAsync(dispatchId, ct);
      if (state.Plan is null || state.Plan.InputsChanged)
        throw new RoutePlanningException("A saved route matching the assigned stops is required before calculating fuel.");
      if (state.Plan.Tracking.AllStopsPassed)
        throw new RoutePlanningException("This dispatch is complete. Select the next dispatch to plan fuel.");
      await fuel.BuildAsync(dispatchId, new(state.Profile), ct);
      cache.Remove($"automatic-planning-error:{dispatchId}:{PlanningSettingsService.Signature(state.Profile)}");
      return await planningReads.ForDispatchAsync(dispatchId, ct);
    }
    finally { gate.Release(); }
  }

  public static void ProjectRecommendations(RoutePlanningState state, RouteGeometry? exactGeometry = null)
  {
        if (state.Plan?.FuelRecommendations is { } recommendations)
        {
          var position = MatchingProgress(state, exactGeometry);
          recommendations.Stations = recommendations.Stations.Where(x => position is null || x.RouteMile > position + .5).ToList();
          foreach (var station in recommendations.Stations)
            station.MilesAhead = state.Progress?.ProgressMiles is { } progress ? station.RouteMile - progress : null;
          if (recommendations.Stations.Count == 0)
            recommendations.Message = "No suitable BVD stations ahead on the planned route.";
        }
  }

  public async Task PrepareUpcomingAsync(Guid dispatchId, CancellationToken ct)
  {
    var gate = gates.For((await plans.LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      var state = await plans.GetAsync(dispatchId, ct);
      if (state.Plan is null || state.Plan.InputsChanged)
      {
        await plans.BuildAsync(dispatchId, new(state.Profile), ct);
        state = await plans.GetAsync(dispatchId, ct);
      }
    }
    finally { gate.Release(); }
  }


  private static double? MatchingProgress(RoutePlanningState state, RouteGeometry? exactGeometry) => state.Progress?.ProgressMiles
    ?? (state.Progress is { LocationStale: false, Position: { } position } && state.Plan is { } plan
      ? (exactGeometry ?? new RouteGeometry(plan.Route)).Match(position).Along : null);
}
