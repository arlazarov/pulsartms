using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

public static class ExecutionPlanningChanges
{
  public static void RouteSaved(
    IAppDbContext db,
    DispatchEntity load,
    DateTime now
  ) =>
    RouteSaved(
      db,
      load.Id,
      load.ExecutionLegId,
      load.TruckId,
      load.AssignmentRevision,
      now
    );

  public static void RouteSaved(
    IAppDbContext db,
    Guid dispatchId,
    Guid? executionLegId,
    Guid? truckId,
    long assignmentRevision,
    DateTime now
  )
  {
    if (executionLegId is not { } legId || truckId is not { } truck)
      return;
    db.ExecutionPlanningChanges.Add(
      new ExecutionPlanningChange
      {
        DispatchId = dispatchId,
        TruckId = truck,
        ExecutionLegId = legId,
        AssignmentRevision = assignmentRevision,
        RequestedAt = now,
        AvailableAt = now,
        MileageOnly = true,
      }
    );
  }

  public static void Enqueue(
    IAppDbContext db,
    IEnumerable<ExecutionLeg> legs,
    DateTime now,
    IReadOnlyDictionary<Guid, Guid>? previousTrucks = null
  )
  {
    foreach (var leg in legs.DistinctBy(x => x.Id))
    foreach (var load in leg.Loads.DistinctBy(x => x.DispatchId))
    {
      if (previousTrucks?.TryGetValue(leg.Id, out var previous) == true)
        db.ExecutionPlanningChanges.Add(
          new ExecutionPlanningChange
          {
            DispatchId = load.DispatchId,
            TruckId = previous,
            ExecutionLegId = leg.Id,
            AssignmentRevision = leg.Revision - 1,
            RequestedAt = now,
            AvailableAt = now,
          }
        );
      db.ExecutionPlanningChanges.Add(
        new ExecutionPlanningChange
        {
          DispatchId = load.DispatchId,
          TruckId = leg.TruckId,
          ExecutionLegId = leg.Id,
          AssignmentRevision = leg.Revision,
          RequestedAt = now,
          AvailableAt = now,
        }
      );
    }
  }
}
