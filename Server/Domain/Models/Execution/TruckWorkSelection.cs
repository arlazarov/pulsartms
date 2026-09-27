using System.Collections.Immutable;

namespace Domain.Models.Execution;

public sealed record WorkVisitReference(Guid Id, string City, string Name);

public sealed record WorkLoadReference(
  Guid Id,
  Guid? ExecutionLegId,
  long AssignmentRevision,
  string? ExecutionStatus,
  int LoadNumber,
  string OrderNumber,
  string CustomerName,
  string DriverName,
  Guid? DriverId,
  WorkOrderKey Order,
  ImmutableArray<WorkVisitReference> Visits
)
{
  // The revision the itinerary records for this work
  // (PlanningWorkPolicy.AcceptedRevision).
  public long AcceptedRevision { get; init; }
}

public sealed record TruckWorkSelection(
  string Key,
  Guid? TruckId,
  string TruckNumber,
  string DriverName,
  string TrailerNumber,
  ImmutableArray<WorkLoadReference> Loads
);
