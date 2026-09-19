using System.Collections.Immutable;

namespace Application.Features.Execution.Models;

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
);

public sealed record TruckWorkSelection(
  string Key,
  Guid? TruckId,
  string TruckNumber,
  string DriverName,
  string TrailerNumber,
  ImmutableArray<WorkLoadReference> Loads
);
