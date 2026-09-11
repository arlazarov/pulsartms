using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Options;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Queries;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningReadService(RoutePlanningService routes, PlanningRefreshQueue refresh,
  ISender mediator, IOptions<SynchronizationOptions> options, Application.Features.Eta.Services.EtaService eta, TruckFuelPlans fuelPlans)
{
  public static void TrimForDisplay(RoutePlan plan, Guid? knownPlanId = null, int? knownVersion = null)
  {
    if (plan.FuelPlan is { } fuel) fuel.RouteChecks = [];
    plan.GeometryOmitted = knownPlanId == plan.Id && knownVersion == plan.Version;
    if (plan.ReferenceRoute is { } reference)
    {
      reference.Points = [];
      reference.Legs = reference.Legs.Select(leg => leg with { Points = plan.GeometryOmitted ? [] : DisplayRouteGeometry.Simplify(leg.Points) }).ToList();
    }
    plan.Route.Points = [];
    plan.Route.Legs = plan.Route.Legs.Select(leg => leg with { Points = plan.GeometryOmitted ? [] : DisplayRouteGeometry.Simplify(leg.Points) }).ToList();
  }

  public async Task<AutomaticPlanningResult> ForTruckAsync(Guid truckId, CancellationToken ct, Guid? knownPlanId = null, int? knownVersion = null)
  {
    var board = await mediator.Send(new GetDispatchBoardQuery(TruckId: truckId, IncludeHos: true, IncludeFinancials: false), ct);
    if (!board.Success) throw new RoutePlanningException("Dispatch assignments are temporarily unavailable.");
    foreach (var load in board.Response?.Items.FirstOrDefault()?.Dispatches ?? [])
    {
      var result = await ReadDispatchAsync(load.Id, board.Response?.Items.FirstOrDefault()?.Hos, ct, knownPlanId, knownVersion);
      CheckAssignments(result.State?.Plan?.FuelPlan, board.Response?.Items.FirstOrDefault()?.Dispatches);
      if (result.TruckId != truckId) return new(truckId, load.Id, load.LoadNumber, null, "This load has multiple truck assignments.");
      if (result.State?.Plan is not { Tracking.AllStopsPassed: true, InputsChanged: false })
      {
        await fuelPlans.ApplyAsync(result.State, ct);
        return result;
      }
    }
    return new(truckId, null, null, null, "No remaining stops in current or upcoming dispatches.") { Hos = board.Response?.Items.FirstOrDefault()?.Hos };
  }

  public async Task<AutomaticPlanningResult> ForDispatchAsync(Guid id, CancellationToken ct, Guid? knownPlanId = null, int? knownVersion = null)
  {
    var load = await routes.LoadAsync(id, ct);
    var board = await mediator.Send(new GetDispatchBoardQuery(TruckId: load.TruckId, IncludeHos: true, IncludeFinancials: false), ct);
    var clocks = board.Response?.Items.FirstOrDefault(x => x.TruckId == load.TruckId)?.Hos;
    var result = await ReadDispatchAsync(id, clocks, ct, knownPlanId, knownVersion, load);
    CheckAssignments(result.State?.Plan?.FuelPlan, board.Success ? board.Response?.Items.FirstOrDefault()?.Dispatches : null);
    await fuelPlans.ApplyAsync(result.State, ct);
    return result;
  }

  private static void CheckAssignments(FuelPlan? fuel, IEnumerable<Application.Features.Dispatch.Models.DispatchResponse>? loads)
  {
    if (fuel is null) return;
    if (loads is null || FuelHorizon.Signature(loads) != fuel.AssignmentSignature)
    {
      fuel.NeedsRefresh = true;
      fuel.RefreshReasons.Add(loads is null ? "Dispatch assignments could not be verified." : "Assigned trips changed. Recalculate fuel.");
    }
  }

  private async Task<AutomaticPlanningResult> ReadDispatchAsync(Guid id,
    Application.Features.Fleet.Models.DriverHosClocks? clocks, CancellationToken ct,
    Guid? knownPlanId, int? knownVersion, Domain.Entities.Dispatch.Dispatch? loaded = null)
  {
    var load = loaded ?? await routes.LoadAsync(id, ct);
    var state = await routes.GetAsync(load, ct, cachedTelemetryOnly: true, displayOnly: true,
      knownPlanId: knownPlanId, knownVersion: knownVersion);
    if (!options.Value.Enabled) refresh.Enqueue(id, state);
    state = state with { Eta = eta.GetCached(state) };
    return new(load.TruckId!.Value, id, load.LoadNumber, state,
      refresh.Message(id, state)) { Hos = clocks };
  }
}
