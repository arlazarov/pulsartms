using Domain.Entities.Dispatch;

namespace Application.Features.Routing.Models;

public sealed record NextLoadRouteVersion(
  Guid DispatchId,
  string? BaseInputHash,
  DateTime? BaseCalculatedAt,
  Guid? PreviousDispatchId,
  string? DeadheadInputHash,
  DateTime? DeadheadCalculatedAt,
  decimal? EmptyMiles
)
{
  public Guid? ExecutionLegId { get; init; }
  public Guid? PreviousExecutionLegId { get; init; }
  public bool HasBaseGeometry { get; init; }
  public bool HasDeadheadGeometry { get; init; }

  public static NextLoadRouteVersion From(
    Guid dispatchId,
    Guid? executionLegId,
    SavedNextLoadRoute? saved
  ) =>
    new(
      dispatchId,
      saved?.BaseRoute?.InputHash,
      saved?.BaseRoute?.CalculatedAt,
      saved?.Deadhead?.PreviousDispatchId,
      saved?.Deadhead?.InputHash,
      saved?.Deadhead?.CalculatedAt,
      saved?.Deadhead?.Miles
    )
    {
      ExecutionLegId = executionLegId,
      PreviousExecutionLegId = saved?.Deadhead?.PreviousExecutionLegId,
      HasBaseGeometry = !string.IsNullOrEmpty(saved?.BaseRoute?.RouteJson),
      HasDeadheadGeometry = !string.IsNullOrEmpty(saved?.Deadhead?.RouteJson),
    };
}

public sealed record SavedNextLoadRoute(
  Guid DispatchId,
  DispatchBaseRoute? BaseRoute,
  DispatchDeadhead? Deadhead
)
{
  public Guid? ExecutionLegId { get; init; }
}
