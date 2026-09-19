namespace Application.Features.Routing.Models;

public sealed record FuelBuildRequest(
  TruckRouteProfile Profile,
  double? CurrentGallons = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
  internal DateTime? AutomaticRefreshRevision { get; init; }
}
