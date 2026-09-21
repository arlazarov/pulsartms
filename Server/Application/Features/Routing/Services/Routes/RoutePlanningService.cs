using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RoutePlanningService(
  IAppDbContext db,
  IRoutingProvider routing,
  ISender mediator,
  TruckPlanningProfileService profiles,
  RoutePlanStore store,
  RouteRecalculationBudget recalculationBudget,
  ReadCache reads,
  IOptions<FuelRegionOptions> regionOptions,
  IOptions<SynchronizationOptions> syncOptions,
  RouteDisplayCache displays,
  BaseRouteService baseRoutes,
  TruckPlanningInputsReader inputs,
  PlanningWorkPublication publication
) : IPlannedRouteReader
{
  private static readonly KeyedGates BuildGates = new();

  private sealed record DispatchSource(
    DispatchEntity Load,
    bool HasNativeExecution
  );

  public async Task<TruckRouteProfile> SaveProfileAsync(
    Guid dispatchId,
    TruckRouteProfile profile,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var (work, legId) = await CaptureWorkAsync(dispatchId, ct, executionLegId);
    var load = PlanningWorkPolicy.Resolve(work, dispatchId, legId);
    if (
      assignmentRevision.HasValue
      && load.AssignmentRevision != assignmentRevision.Value
    )
      throw new RoutePlanningException(
        "The truck assignment changed. Refresh before saving its profile."
      );
    await using var transaction = await publication.BeginAsync(work, ct);
    var saved = await profiles.SaveAsync(work.TruckId, profile, ct);
    await transaction.CommitAsync(ct);
    profiles.Invalidate(work.TruckId);
    return saved;
  }

  internal async Task<(
    TruckItinerarySnapshot Itinerary,
    Guid? ExecutionLegId
  )> CaptureWorkAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null,
    Guid? truckId = null
  )
  {
    var located = await LoadAsync(dispatchId, ct, executionLegId, truckId);
    if (truckId.HasValue && located.TruckId != truckId)
      throw new RoutePlanningException("The truck assignment changed.");
    var captured =
      await inputs.ReadFreshAsync(located.TruckId!.Value, ct)
      ?? throw new RoutePlanningException("The assigned truck is unavailable.");
    PlanningWorkPolicy.Resolve(
      captured.Itinerary,
      dispatchId,
      located.ExecutionLegId
    );
    return (captured.Itinerary, located.ExecutionLegId);
  }

  // The contract keeps only what a consumer outside the route lifecycle
  // needs; the wider overloads stay internal to routing.
  Task<RoutePlanningState> IPlannedRouteReader.GetAsync(
    RouteWorkSnapshot work,
    CancellationToken ct,
    PlannedRouteTelemetry telemetry
  ) =>
    GetAsync(
      work,
      ct,
      cachedTelemetryOnly: telemetry is PlannedRouteTelemetry.Cached,
      withoutProviderWait: telemetry
        is PlannedRouteTelemetry.WithoutProviderWait
    );

  Task<RouteWorkSnapshot> IPlannedRouteReader.LoadAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId
  ) => LoadAsync(dispatchId, ct, executionLegId);
}
