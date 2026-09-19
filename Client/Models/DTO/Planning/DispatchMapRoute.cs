namespace Client.Models.DTO.Planning;

public sealed record DispatchMapRoute(
  Guid DispatchId,
  List<DispatchMapSegment> Segments,
  int MissingSections
);

public sealed record DispatchMapSegment(
  Guid FromStopId,
  Guid ToStopId,
  List<RoutePoint> Points
)
{
  public RouteSegmentMeaning Meaning { get; init; } = new("Unknown", "Unknown");
}
