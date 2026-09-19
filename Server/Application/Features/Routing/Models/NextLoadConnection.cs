namespace Application.Features.Routing.Models;

public sealed record NextLoadConnection(
  double Miles,
  IReadOnlyList<RoutePoint> Points
)
{
  public static NextLoadConnection? From(TruckRoute? route)
  {
    if (route is null || !double.IsFinite(route.Miles) || route.Miles < 0)
      return null;
    var points =
      route.Points.Count > 1
        ? route.Points
        : route.Legs.SelectMany(leg => leg.Points).ToList();
    return points.Count > 1 && points.All(point => point.IsValid)
      ? new(route.Miles, points)
      : null;
  }
}
