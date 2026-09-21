namespace Domain.Models.Routing;

public readonly record struct PlanningScope(
  Guid DispatchId,
  Guid? ExecutionLegId,
  long AssignmentRevision = 0
)
{
  public Guid StorageKey => ExecutionLegId ?? DispatchId;

  public string CacheKey =>
    ExecutionLegId is { } legId
      ? $"route:{DispatchId}:leg:{legId}"
      : $"route:{DispatchId}";
}
