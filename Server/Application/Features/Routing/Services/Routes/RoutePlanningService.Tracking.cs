using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

// Following a truck along its saved road: which stops it has passed, and
// giving it a new road when RerouteDecision says it needs one.
public sealed partial class RoutePlanningService
{
  public async Task<bool> AdvanceAutomaticallyAsync(
    Guid dispatchId,
    CancellationToken ct,
    bool forceReroute = false,
    Guid? executionLegId = null,
    Guid? truckId = null,
    TruckItinerarySnapshot? capturedWork = null
  )
  {
    var work = capturedWork;
    if (work is null)
      (work, executionLegId) = await CaptureWorkAsync(
        dispatchId,
        ct,
        executionLegId,
        truckId
      );
    var gate = BuildGates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var load = PlanningWorkPolicy.Resolve(work, dispatchId, executionLegId);
      var profile = await ProfileAsync(work.TruckId, ct);
      if (
        !await PlanningWorkPolicy.IsCurrentAsync(work, load, store, profile, ct)
      )
        return false;
      var recent = await mediator.Send(new GetFleetLocationsQuery(), ct);
      var truck = recent.Response?.Trucks.FirstOrDefault(x =>
        x.TruckId == load.TruckId
      );
      var entity =
        await store.ReadAsync(dispatchId, ct, load.ExecutionLegId)
        ?? throw new RoutePlanningException("Route not found.");
      var plan = JsonSerializer.Deserialize<RoutePlan>(
        entity.PlanJson,
        RoutingJson.Options
      )!;
      if (
        entity.TruckId != work.TruckId
        || plan.TruckId != work.TruckId
        || !RoutePlanInputs.Matches(entity, load, profile)
      )
        throw new RoutePlanningException(
          "The saved route no longer matches the truck work. Rebuild it first."
        );
      var now = DateTime.UtcNow;
      var before = JsonSerializer.Serialize(plan.Tracking, RoutingJson.Options);
      foreach (
        var point in (recent.Response?.Points ?? [])
          .Where(x =>
            x.TruckId == load.TruckId && x.UpdatedAt <= truck?.UpdatedAt
          )
          .OrderBy(x => x.UpdatedAt)
      )
        RouteStopTracker.Update(plan, load, point, now);
      RouteStopTracker.Update(plan, load, truck, now);
      var sync = syncOptions.Value;
      var verdict = RerouteDecision.Judge(
        plan,
        load,
        truck,
        sync.RouteDeviationMiles,
        sync.RouteDeviationSeconds,
        now,
        forceReroute
      );
      var progress = verdict.Progress;
      var remainingStops = verdict.RemainingStops;
      if (verdict.Reroute)
      {
        await recalculationBudget.ReserveAsync(
          plan.TruckId,
          progress.Position!,
          ct
        );
        var route = await baseRoutes.CurrentAsync(
          load,
          plan.Profile,
          progress.Position!,
          remainingStops,
          ct
        );
        if (!plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
          plan.OriginalPlannedMiles = route.Miles;
        plan.ReferenceRoute ??= plan.Route;
        plan.ReferenceStops ??= plan.Stops;
        plan.ReferenceRoute = await RouteDisplayReference.ReconnectAsync(
          plan.ReferenceRoute,
          plan.ReferenceStops,
          remainingStops,
          route,
          plan.Profile,
          routing,
          ct
        );
        plan.Route = route;
        plan.Stops = remainingStops;
        plan.FromCurrentPosition = true;
        plan.CalculatedAt = route.CalculatedAt;
        plan.LastReroutedAt = now;
        plan.LastReroutePosition = progress.Position;
        plan.Tracking.OffRouteSince = null;
        plan.Version++;
        if (plan.FuelPlan is { } previousFuel)
          previousFuel.NeedsRefresh = true;
        plan.FuelRecommendations = null;
      }
      if (plan.Tracking.AllStopsPassed)
      {
        plan.FuelPlan = null;
        plan.FuelRecommendations = null;
      }
      if (
        before != JsonSerializer.Serialize(plan.Tracking, RoutingJson.Options)
        || plan.LastReroutedAt == now
      )
      {
        await using var transaction = await publication.BeginAsync(work, ct);
        await profiles.RequireRoutingCurrentAsync(load, profile, ct);
        await store.SaveAsync(entity, plan, ct);
        await transaction.CommitAsync(ct);
        store.Invalidate(dispatchId, load.ExecutionLegId);
        return true;
      }
      return false;
    }
    finally
    {
      gate.Release();
    }
  }
}
