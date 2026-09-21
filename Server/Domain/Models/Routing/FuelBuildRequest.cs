namespace Domain.Models.Routing;

public sealed record FuelBuildRequest(
  TruckRouteProfile Profile,
  double? CurrentGallons = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
  public DateTime? AutomaticRefreshRevision { get; init; }
}
