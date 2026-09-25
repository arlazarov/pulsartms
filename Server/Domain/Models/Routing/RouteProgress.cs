namespace Domain.Models.Routing;

public sealed record RouteProgress(
  double? ProgressMiles,
  double? RemainingMiles,
  double? RemainingSeconds,
  double DistanceFromRouteMiles,
  bool OffRoute,
  bool LocationStale,
  DateTime? LocationTime,
  RoutePoint? Position
)
{
  // The truck's speed in the telemetry reading this was measured from;
  // null without one.
  public double? SpeedMph { get; init; }
}
