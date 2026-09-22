using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public static partial class RoutePlanStorage
{
  private static void PreserveMovement(DispatchRoutePlan entity, RoutePlan plan)
  {
    if (plan.Tracking.Movement is not null || plan.CompletedMovement.Count > 0)
      return;
    var old = SavedRouteReader.Plan(entity.PlanJson);
    if (old?.Tracking.Movement is not { } open)
      return;
    plan.Tracking.Movement = open;
    if (old.TruckId == plan.TruckId)
      plan.Tracking.LastObservationAt ??= old.Tracking.LastObservationAt;
    else
      RouteMovementRecorder.Close(plan);
  }

  private static void SaveMovement(
    IAppDbContext db,
    DispatchRoutePlan entity,
    RoutePlan plan
  )
  {
    if (
      plan.Tracking.AllStopsPassed
      || plan.Tracking.Movement is { } open
        && (
          open.TruckId != plan.TruckId
          || open.NextStopId != plan.Tracking.NextStopId
          || open.GeometryRevision != entity.GeometryRevision
        )
    )
      RouteMovementRecorder.Close(plan);
    foreach (var movement in plan.CompletedMovement)
    {
      if (movement.Observations.Count == 0)
        continue;
      db.RouteMovementChunks.Add(
        new()
        {
          Id = movement.Id,
          CompanyId = entity.CompanyId,
          RoutePlanId = entity.Id,
          TruckId = movement.TruckId,
          From = movement.Observations[0].At,
          To = movement.Observations[^1].At,
          MovementJson = JsonSerializer.Serialize(
            movement,
            RoutingJson.Options
          ),
        }
      );
    }
    plan.CompletedMovement.Clear();
  }
}
