using System.Linq.Expressions;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

public static class MovementIntervals
{
  public static Expression<Func<Movement, bool>> OverlappingTruck(
    Guid truckId,
    DateTime? startedAt,
    DateTime? endedAt,
    Guid? exceptMovementId = null
  ) =>
    movement =>
      startedAt.HasValue
      && movement.TruckId == truckId
      && (!exceptMovementId.HasValue || movement.Id != exceptMovementId)
      && movement.StartedAt.HasValue
      && (!endedAt.HasValue || movement.StartedAt < endedAt)
      && (!movement.EndedAt.HasValue || movement.EndedAt > startedAt);

  public static bool ValidActual(
    DateTime? start,
    DateTime? end,
    DateTime observedAt,
    DateTime now
  ) =>
    start.HasValue
    && end.HasValue
    && start.Value.Year >= 2000
    && start < end
    && end <= observedAt
    && observedAt <= now;
}
