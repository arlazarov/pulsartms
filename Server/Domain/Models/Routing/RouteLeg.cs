namespace Domain.Models.Routing;

public sealed record RouteLeg(
  double Miles,
  double Seconds,
  List<RoutePoint> Points
);
