namespace Client.Models.DTO.Planning;

public sealed record FuelBuildRequest(
  TruckRouteProfile Profile,
  double? CurrentGallons = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}
