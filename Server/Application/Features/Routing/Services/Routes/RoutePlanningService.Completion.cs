using System.Collections.Immutable;
using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RoutePlanningService
{
  internal async Task<DispatchRoutePlan?> PrepareCompletionAsync(
    DispatchStop changed,
    CancellationToken ct
  )
  {
    // Read persisted inputs before the caller commits the new completion
    // revision.
    var sourceLoad = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync(x => x.Id == changed.DispatchId, ct);
    RouteWorkSnapshot load;
    try
    {
      load = await ResolveAssignmentAsync(sourceLoad, ct);
    }
    catch (RoutePlanningException)
    {
      return null;
    }
    // Source edits do not confirm native visits; reconciliation owns that link.
    if (load.ExecutionLegId.HasValue)
      return null;
    var entity = await store.ReadForUpdateAsync(
      load.Id,
      ct,
      load.ExecutionLegId
    );
    if (entity is null)
      return null;
    var profile = await ProfileAsync(load.TruckId!.Value, ct);
    if (!RoutePlanInputs.Matches(entity, load, profile))
      return null;
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      entity.PlanJson,
      RoutingJson.Options
    );
    if (plan is null || plan.TruckId != load.TruckId)
      return null;

    var source = load.Stops.SingleOrDefault(x => x.Id == changed.Id);
    if (source is null)
      return null;
    load = load with
    {
      Stops = load
        .Stops.Select(stop =>
          stop.Id == changed.Id
            ? stop with
            {
              ManualCompletedAt = changed.ManualCompletedAt,
              ManualCompletionRevision = changed.ManualCompletionRevision,
            }
            : stop
        )
        .ToImmutableArray(),
    };
    RouteStopTracker.Update(plan, load, null, DateTime.UtcNow);
    var remaining = (plan.ReferenceStops ?? plan.Stops)
      .Where(x => !plan.Tracking.PassedStopIds.Contains(x.Id))
      .Select(x => x.Id)
      .ToArray();
    // Completing a prefix advances along the same road. Skipping an interior
    // visit
    // or restoring a stop absent from that road changes the remaining
    // itinerary.
    if (
      remaining.Length > plan.Stops.Count
      || !plan
        .Stops.TakeLast(remaining.Length)
        .Select(x => x.Id)
        .SequenceEqual(remaining)
    )
      return null;

    plan.InputsChanged = false;
    if (plan.Tracking.AllStopsPassed)
    {
      plan.FuelPlan = null;
      plan.FuelRecommendations = null;
    }
    entity.InputHash = RoutePlanInputs.Hash(load, profile);
    entity.PlanJson = RoutePlanStorage.Serialize(plan);
    // The command saves these concurrency-protected metadata changes atomically
    // with the stop and audit event, without changing the geometry revision.
    return entity;
  }
}
