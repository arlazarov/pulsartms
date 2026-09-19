namespace Application.Features.Routing.Models;

public sealed record RouteLeg(
  double Miles,
  double Seconds,
  List<RoutePoint> Points
);
