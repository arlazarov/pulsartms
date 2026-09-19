using Domain.Entities.Fleet;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class DispatchAssignmentReconciliation
{
  public static bool ReleaseStaleConfirmation(
    DispatchEntity load,
    IReadOnlyDictionary<string, Truck> trucks,
    DateTime importedAt
  )
  {
    if (
      load.PlanningTruckId is not { } confirmed
      || load.PlanningAssignmentRevision == long.MaxValue
      || load.Status is "completed" or "cancelled" or "canceled"
      || load.Stops.Count == 0
      || load.Stops.Any(stop => stop.ManualAction is not null)
      || load.PlanningFromStopId is { } anchor
        && load.Stops.All(stop => stop.Id != anchor)
    )
      return false;

    var assignments = load
      .Stops.Select(stop => (stop.TruckId, stop.TruckNumber))
      .Prepend((load.TruckId, load.TruckNumber))
      .ToArray();
    var ids = assignments
      .Where(assignment => assignment.TruckId.HasValue)
      .Select(assignment => assignment.TruckId!.Value)
      .Distinct()
      .ToArray();
    if (ids.Length != 1 || ids[0] == confirmed)
      return false;
    var truck = trucks.Values.FirstOrDefault(x => x.Id == ids[0]);
    if (
      truck is not { IsActive: true }
      || assignments.Any(assignment => assignment.TruckId != truck.Id)
      || assignments.Any(assignment =>
        !string.IsNullOrWhiteSpace(assignment.TruckNumber)
        && !assignment
          .TruckNumber.Trim()
          .Equals(truck.UnitNumber.Trim(), StringComparison.OrdinalIgnoreCase)
      )
    )
      return false;

    // Confirmation belonged to the former truck, not the imported replacement.
    load.PlanningTruckId = null;
    load.PlanningFromStopId = null;
    load.PlanningAssignmentRecordedAt = importedAt;
    load.PlanningAssignmentRecordedBy = null;
    load.PlanningAssignmentRevision++;
    return true;
  }
}
