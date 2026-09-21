using Application.Caching;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Routing.Services.Routes;

public sealed class AutomaticPlanningService(
  RoutePlanningService plans,
  FuelPlanningService fuel,
  TruckPlanningInputsReader inputs,
  IMemoryCache cache,
  PlanningReadService planningReads
)
{
  private static readonly KeyedGates Gates = new();

  public async Task<AutomaticPlanningResult> ForTruckAsync(
    Guid truckId,
    CancellationToken ct
  )
  {
    var captured = await inputs.ReadFreshAsync(truckId, ct, includeHos: true);
    if (captured is not null)
      foreach (var segment in PlanningWorkPolicy.Candidates(captured.Itinerary))
      {
        var result = await ForWorkAsync(
          captured.Itinerary,
          segment.Work.DispatchId,
          ct,
          connectFromTruck: segment.Work.ExecutionLegId.HasValue
            || segment.Status == "assigned",
          executionLegId: segment.Work.ExecutionLegId
        );
        if (
          !PlanningWorkPolicy.IsCompleted(
            result.State?.Plan,
            PlanningWorkPolicy.Resolve(captured.Itinerary, segment)
          )
        )
          return result with { Hos = captured.Hos };
      }
    return new(
      truckId,
      null,
      null,
      null,
      "No remaining stops in current or upcoming dispatches."
    )
    {
      Hos = captured?.Hos,
    };
  }

  public async Task<AutomaticPlanningResult> ForDispatchAsync(
    Guid dispatchId,
    CancellationToken ct,
    bool connectFromTruck = false,
    Guid? executionLegId = null,
    Guid? truckId = null,
    long? assignmentRevision = null
  )
  {
    var (work, resolvedLegId) = await plans.CaptureWorkAsync(
      dispatchId,
      ct,
      executionLegId,
      truckId
    );
    return await ForWorkAsync(
      work,
      dispatchId,
      ct,
      connectFromTruck,
      resolvedLegId,
      assignmentRevision
    );
  }

  private async Task<AutomaticPlanningResult> ForWorkAsync(
    TruckItinerarySnapshot work,
    Guid dispatchId,
    CancellationToken ct,
    bool connectFromTruck,
    Guid? executionLegId,
    long? assignmentRevision = null
  )
  {
    var gate = Gates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var load = PlanningWorkPolicy.Resolve(work, dispatchId, executionLegId);
      if (
        executionLegId.HasValue
        && assignmentRevision.HasValue
        && load.AssignmentRevision != assignmentRevision
      )
        throw new RoutePlanningException("The execution assignment changed.");
      var state = await plans.GetAsync(load, ct);
      var key = PlanningRefreshQueue.ErrorKey(
        dispatchId,
        state,
        work.InputSignature,
        load.ExecutionLegId,
        load.AssignmentRevision
      );
      if (cache.TryGetValue<string>(key, out var previousError))
        return Result(state, previousError);
      try
      {
        var built = false;
        if (state.Plan is null || state.Plan.InputsChanged)
        {
          var next = load
            .Stops.Where(s => !s.IsCompleted)
            .OrderBy(s => s.Sequence)
            .FirstOrDefault();
          var fromCurrent =
            PlanningWorkPolicy.CanUseGps(load)
            && next is not null
            && (
              load.Stops.Any(s => s.ManualCompletionRevision > 0)
              || connectFromTruck
              || load.Stops.Length == 1
                && (
                  connectFromTruck
                  || PlanningWorkPolicy.Candidates(work).FirstOrDefault()?.Work
                    == new WorkIdentity(load.Id, load.ExecutionLegId)
                )
            );
          await plans.BuildAsync(
            dispatchId,
            new(state.Profile, fromCurrent, fromCurrent ? next!.Sequence : null)
            {
              ExecutionLegId = load.ExecutionLegId,
            },
            ct,
            automatic: true,
            capturedWork: work
          );
          built = true;
        }
        var advanced = await plans.AdvanceAutomaticallyAsync(
          dispatchId,
          ct,
          forceReroute: connectFromTruck
            && state.Plan is { FromCurrentPosition: false }
            && (
              load.ExecutionLegId.HasValue
              || state.Plan.Tracking.PassedStopIds.Count == 0
                && !load.Stops.Any(stop => stop.IsCompleted)
            ),
          executionLegId: load.ExecutionLegId,
          capturedWork: work
        );
        if (built || advanced)
          state = await plans.GetAsync(load, ct);
        var plan = state.Plan!;
        if (plan.Tracking.AllStopsPassed)
          return Result(state, "All dispatch stops passed.");
        // Route tracking does not replace fuel purchases. The lease-owned
        // refresh handles assignment and quote changes separately.
        return Result(state, null);
      }
      catch (Exception ex)
        when (ex is RoutePlanningException or HttpRequestException)
      {
        var message =
          ex is RoutePlanningException
            ? ex.Message
            : "Route service is temporarily unavailable. Retrying automatically.";
        cache.Set(key, message, TimeSpan.FromMinutes(2));
        return Result(state, message);
      }

      AutomaticPlanningResult Result(RoutePlanningState value, string? message)
      {
        ProjectRecommendations(value);
        return PlanningWorkPolicy.WithWarnings(
          new(load.TruckId!.Value, load.Id, load.LoadNumber, value, message)
          {
            ExecutionLegId = load.ExecutionLegId,
            AssignmentRevision = load.AssignmentRevision,
          },
          work.Segments.Single(x =>
            x.Work == new WorkIdentity(load.Id, load.ExecutionLegId)
          )
        );
      }
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<AutomaticPlanningResult> RecalculateFuelAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null,
    DateTime? automaticRefreshRevision = null
  )
  {
    var (work, resolvedLegId) = await plans.CaptureWorkAsync(
      dispatchId,
      ct,
      executionLegId
    );
    var gate = Gates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      // Replacement is committed only after validation; reads independently
      // reject invalid older plans.
      await inputs.RequireCurrentAsync(work, ct);
      var load = PlanningWorkPolicy.Resolve(work, dispatchId, resolvedLegId);
      if (
        load.ExecutionLegId.HasValue
        && (
          load.ExecutionLegId != executionLegId
          || load.AssignmentRevision != assignmentRevision
          || !PlanningWorkPolicy.CanUseGps(load)
        )
      )
        throw new PlanningSettingsConflictException(
          "Execution changed. Reopen the current truck fuel plan."
        );
      var state = await plans.GetAsync(load, ct);
      if (state.Plan is null || state.Plan.InputsChanged)
        throw new RoutePlanningException(
          "A saved route matching the assigned stops is required before calculating fuel."
        );
      if (state.Plan.Tracking.AllStopsPassed)
        throw new RoutePlanningException(
          "This dispatch is complete. Select the next dispatch to plan fuel."
        );
      var calculation = await fuel.BuildAsync(
        dispatchId,
        new(state.Profile)
        {
          ExecutionLegId = executionLegId,
          AssignmentRevision = assignmentRevision,
          AutomaticRefreshRevision = automaticRefreshRevision,
        },
        ct
      );
      cache.Remove(
        PlanningRefreshQueue.ErrorKey(dispatchId, state, work.InputSignature)
      );
      var result = await planningReads.ForDispatchAsync(
        dispatchId,
        ct,
        executionLegId: executionLegId,
        truckId: load.TruckId
      );
      return result with
      {
        FuelStatus = calculation.Status,
        Message = calculation.Access?.Message ?? result.Message,
      };
    }
    finally
    {
      gate.Release();
    }
  }

  public static void ProjectRecommendations(
    RoutePlanningState state,
    RouteGeometry? exactGeometry = null
  )
  {
    if (state.Plan?.FuelRecommendations is { } recommendations)
    {
      if (recommendations.AccessProblem)
        return;
      var position = MatchingProgress(state, exactGeometry);
      recommendations.Stations = recommendations
        .Stations.Where(x => position is null || x.RouteMile > position + .5)
        .ToList();
      foreach (var station in recommendations.Stations)
        station.MilesAhead = state.Progress?.ProgressMiles is { } progress
          ? station.RouteMile - progress
          : null;
      if (recommendations.Stations.Count == 0)
        recommendations.Message =
          "No suitable BVD stations ahead on the planned route.";
    }
  }

  public async Task PrepareUpcomingAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null,
    Guid? truckId = null
  )
  {
    var (work, resolvedLegId) = await plans.CaptureWorkAsync(
      dispatchId,
      ct,
      executionLegId,
      truckId
    );
    var gate = Gates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "AutomaticPlanning", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var load = PlanningWorkPolicy.Resolve(work, dispatchId, resolvedLegId);
      var state = await plans.GetAsync(load, ct);
      if (state.Plan is null || state.Plan.InputsChanged)
      {
        await plans.BuildAsync(
          dispatchId,
          new(state.Profile) { ExecutionLegId = load.ExecutionLegId },
          ct,
          capturedWork: work
        );
      }
    }
    finally
    {
      gate.Release();
    }
  }

  private static double? MatchingProgress(
    RoutePlanningState state,
    RouteGeometry? exactGeometry
  ) =>
    state.Progress?.ProgressMiles
    ?? (
      state.Progress is { LocationStale: false, Position: { } position }
      && state.Plan is { } plan
        ? (exactGeometry ?? new RouteGeometry(plan.Route)).Match(position).Along
        : null
    );
}
