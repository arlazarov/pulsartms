using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Services;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Options;
using Domain.Models.Execution;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningReadService(
  RoutePlanningService routes,
  TruckPlanningInputsReader inputs,
  PlanningRefreshQueue refresh,
  ISender mediator,
  IOptions<SynchronizationOptions> options,
  EtaService eta,
  TruckFuelPlans fuelPlans
)
{
  public static void TrimForDisplay(
    RoutePlan plan,
    Guid? knownPlanId = null,
    int? knownVersion = null
  )
  {
    if (plan.FuelPlan is { } fuel)
      fuel.RouteChecks = [];
    plan.GeometryOmitted =
      knownPlanId == plan.Id && knownVersion == plan.Version;
    if (plan.ReferenceRoute is { } reference)
    {
      reference.Points = [];
      reference.Legs = reference
        .Legs.Select(leg =>
          leg with
          {
            Points = plan.GeometryOmitted
              ? []
              : DisplayRouteGeometry.Simplify(leg.Points),
          }
        )
        .ToList();
      if (
        plan.FromCurrentPosition
        && plan.Route.Legs.FirstOrDefault()?.Points.FirstOrDefault()
          is { } origin
      )
        reference.Legs = DisplayRouteGeometry.TravelledHead(
          reference.Legs,
          origin
        );
    }
    plan.Route.Points = [];
    plan.Route.Legs = plan
      .Route.Legs.Select(leg =>
        leg with
        {
          Points = plan.GeometryOmitted
            ? []
            : DisplayRouteGeometry.Simplify(leg.Points),
        }
      )
      .ToList();
  }

  public async Task<AutomaticPlanningResult> ForTruckAsync(
    Guid truckId,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null
  )
  {
    var work = await inputs.ReadAsync(truckId, ct);
    return work is null
      ? NoRemaining(truckId, null)
      : await ForItineraryAsync(work, ct, knownPlanId, knownVersion);
  }

  public async Task<List<AutomaticPlanningResult>> ForBoardAsync(
    GetDispatchBoardQuery query,
    CancellationToken ct
  )
  {
    var board = await mediator.Send(
      query with
      {
        PageSize = 12,
        IncludeFinancials = false,
        IncludeHos = false,
        IncludeEta = false,
        IncludePlanned = false,
        IdentitiesOnly = false,
      },
      ct
    );
    if (!board.Success)
      throw new RoutePlanningException(
        "Dispatch assignments are temporarily unavailable."
      );
    var snapshots = await inputs.ReadManyAsync(
      (board.Response?.Items ?? [])
        .Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId!.Value)
        .Distinct()
        .ToArray(),
      ct
    );
    var results = new List<AutomaticPlanningResult>();
    foreach (var work in snapshots.Values)
    {
      var first = PlanningWorkPolicy
        .Candidates(work.Itinerary)
        .FirstOrDefault();
      if (first is null)
        continue;
      try
      {
        results.Add(await ForItineraryAsync(work, ct, metadataOnly: true));
      }
      catch (RoutePlanningException ex)
      {
        results.Add(
          new(
            work.Itinerary.TruckId,
            first.Work.DispatchId,
            first.LoadNumber,
            null,
            ex.Message
          )
          {
            Hos = work.Hos,
            ExecutionLegId = first.Work.ExecutionLegId,
            AssignmentRevision = first.Work.ExecutionLegId.HasValue
              ? first.AssignmentRevision
              : 0,
          }
        );
      }
    }
    return results;
  }

  private async Task<AutomaticPlanningResult> ForItineraryAsync(
    TruckPlanningInputs work,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool metadataOnly = false
  )
  {
    var snapshot = work.Itinerary;
    foreach (var segment in PlanningWorkPolicy.Candidates(snapshot))
    {
      var load = PlanningWorkPolicy.Resolve(snapshot, segment);
      var result = await ReadDispatchAsync(
        load,
        work.Hos,
        snapshot.InputSignature,
        ct,
        knownPlanId,
        knownVersion,
        metadataOnly
      );
      CheckAssignments(result.State?.Plan, snapshot);
      if (!PlanningWorkPolicy.IsCompleted(result.State?.Plan, load))
      {
        await ApplyFuelAsync(result.State, ct, snapshot);
        if (metadataOnly && result.State?.Plan is { } plan)
        {
          TrimForDisplay(plan, plan.Id, plan.Version);
          plan.FuelRecommendations = null;
        }
        return PlanningWorkPolicy.WithWarnings(result, segment);
      }
    }
    return NoRemaining(snapshot.TruckId, work.Hos);
  }

  private static AutomaticPlanningResult NoRemaining(
    Guid truckId,
    DriverHosClocks? clocks
  ) =>
    new(
      truckId,
      null,
      null,
      null,
      "No remaining stops in current or upcoming dispatches."
    )
    {
      Hos = clocks,
    };

  public async Task<AutomaticPlanningResult> ForDispatchAsync(
    Guid id,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    Guid? executionLegId = null,
    Guid? truckId = null
  )
  {
    var load = await routes.LoadAsync(id, ct, executionLegId, truckId);
    var work = await inputs.ReadAsync(load.TruckId!.Value, ct);
    var segment = work?.Itinerary.Segments.FirstOrDefault(x =>
      x.Work.DispatchId == id && x.Work.ExecutionLegId == load.ExecutionLegId
    );
    if (segment is not null)
      load = PlanningWorkPolicy.Resolve(work!.Itinerary, segment);
    else if (work?.Itinerary.Segments.Any(x => x.Work.DispatchId == id) == true)
      throw new RoutePlanningException(
        "The truck assignment changed. Refresh the route."
      );
    var result = await ReadDispatchAsync(
      load,
      work?.Hos,
      work?.Itinerary.InputSignature,
      ct,
      knownPlanId,
      knownVersion
    );
    CheckAssignments(result.State?.Plan, work?.Itinerary);
    await ApplyFuelAsync(result.State, ct, work?.Itinerary);
    return PlanningWorkPolicy.WithWarnings(result, segment);
  }

  private async Task ApplyFuelAsync(
    RoutePlanningState? state,
    CancellationToken ct,
    TruckItinerarySnapshot? itinerary
  )
  {
    await fuelPlans.ApplyAsync(state, ct, itinerary);
    if (state is not null)
      state.FuelStopArrivals = FuelArrivalForecast.Calculate(state);
  }

  private static void CheckAssignments(
    RoutePlan? plan,
    TruckItinerarySnapshot? itinerary
  )
  {
    if (plan?.FuelPlan is not { } fuel)
      return;
    var loads = itinerary is null
      ? null
      : new FuelWorkInputs(itinerary).SelectForDisplay(plan);
    if (
      loads is null
      || FuelWorkSignature.Signature(loads) != fuel.AssignmentSignature
    )
    {
      fuel.NeedsRefresh = true;
      fuel.RefreshReasons.Add(
        loads is null
          ? "Dispatch assignments could not be verified."
          : "Assigned trips changed. Recalculate fuel."
      );
    }
  }

  private async Task<AutomaticPlanningResult> ReadDispatchAsync(
    RouteWorkSnapshot load,
    DriverHosClocks? clocks,
    string? inputSignature,
    CancellationToken ct,
    Guid? knownPlanId,
    int? knownVersion,
    bool metadataOnly = false
  )
  {
    var id = load.Id;
    var state = await routes.GetAsync(
      load,
      ct,
      cachedTelemetryOnly: true,
      displayOnly: true,
      knownPlanId: knownPlanId,
      knownVersion: knownVersion,
      metadataOnly: metadataOnly
    );
    var identity = inputSignature ?? RoutePlanInputs.Hash(load, state.Profile);
    PlanningRefreshState? requested = null;
    if (
      !options.Value.Enabled
      || state.Plan is null
      || state.Plan.InputsChanged
    )
      requested = await refresh.EnqueueAsync(
        new(id, load.ExecutionLegId, load.AssignmentRevision),
        state,
        identity,
        ct
      );
    state = state with { Eta = eta.GetCached(state) };
    return new(
      load.TruckId!.Value,
      id,
      load.LoadNumber,
      state,
      refresh.Message(
        id,
        state,
        identity,
        load.ExecutionLegId,
        load.AssignmentRevision,
        requested?.Pending == true
      )
    )
    {
      Hos = clocks,
      ExecutionLegId = load.ExecutionLegId,
      AssignmentRevision = load.AssignmentRevision,
    };
  }
}
