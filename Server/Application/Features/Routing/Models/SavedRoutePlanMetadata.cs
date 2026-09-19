namespace Application.Features.Routing.Models;

public sealed record SavedRoutePlanMetadata(
  string InputHash,
  Guid TruckId,
  Guid PlanTruckId,
  Guid PlanId,
  int Version,
  RouteStopTracking Tracking,
  DateTime? FuelCalculatedAt
)
{
  public Guid DispatchId { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public Guid? PlanExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }
  public long StoredAssignmentRevision { get; init; }
  public Guid PlanDispatchId { get; init; }
  public bool FromCurrentPosition { get; init; }
}
