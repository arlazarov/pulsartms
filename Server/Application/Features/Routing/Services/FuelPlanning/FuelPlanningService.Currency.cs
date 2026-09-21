using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// Whether the work a request was made against is still the work. A fuel
// plan is calculated over seconds and edited over minutes, and in that time
// a dispatcher can reassign the truck: every one of these refuses rather
// than write a plan for an assignment that is no longer there.
public sealed partial class FuelPlanningService
{
  private async Task<RouteWorkSnapshot> FuelLoadAsync(
    Guid dispatchId,
    Guid? executionLegId,
    long? assignmentRevision,
    CancellationToken ct
  )
  {
    var load = await plans.LoadAsync(dispatchId, ct, executionLegId);
    if (
      executionLegId == Guid.Empty
      || assignmentRevision < 0
      || load.ExecutionLegId.HasValue
        && (
          load.ExecutionLegId != executionLegId
          || load.AssignmentRevision != assignmentRevision
          || !PlanningWorkPolicy.CanUseGps(load)
        )
      || !load.ExecutionLegId.HasValue
        && (executionLegId.HasValue || assignmentRevision is > 0)
    )
      throw new PlanningSettingsConflictException(
        "Execution changed. Reopen the current truck fuel plan."
      );
    return load;
  }

  private async Task RequireCurrentAsync(
    Guid dispatchId,
    Guid truckId,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var captured = await inputs.ReadFreshAsync(truckId, ct);
    await RequireCurrentAsync(
      captured,
      dispatchId,
      executionLegId,
      assignmentRevision,
      await plans.ProfileAsync(truckId, ct),
      ct
    );
  }

  private async Task RequireCurrentAsync(
    FuelWorkInputs captured,
    Guid dispatchId,
    Guid? executionLegId,
    long? assignmentRevision,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var segment = captured.Itinerary.Segments.SingleOrDefault(x =>
      x.Work.DispatchId == dispatchId && x.Work.ExecutionLegId == executionLegId
    );
    var current = segment is null
      ? null
      : PlanningWorkPolicy.Resolve(captured.Itinerary, segment);
    if (
      current is null
      || executionLegId.HasValue
        && (
          current.AssignmentRevision != assignmentRevision
          || current.Stops.FirstOrDefault()?.AwaitingHandoff == true
        )
      || !await PlanningCurrency.IsCurrentAsync(
        captured.Itinerary,
        current,
        routeStore,
        profile,
        ct
      )
    )
      throw new RoutePlanningException(
        "The truck's current load changed. Reopen its fuel plan."
      );
  }

  private static void RequireRevision(
    TruckFuelPlanSnapshot? saved,
    DateTime? expected
  )
  {
    if (
      expected is { } revision
        && (revision.Kind != DateTimeKind.Utc || revision == default)
      || (saved?.CalculatedAt.Ticks / 10) != (expected?.Ticks / 10)
    )
      throw new PlanningSettingsConflictException(
        "The fuel plan changed in another session. Reopen it before saving."
      );
  }
}
