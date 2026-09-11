using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class SavedRouteGeometry
{
  public static bool Complete(TruckRoute? route, int legCount) => route is not null
    && double.IsFinite(route.Miles) && route.Miles >= 0 && double.IsFinite(route.Seconds) && route.Seconds >= 0
    && route.Points is { } points && points.All(point => point is not null && point.IsValid)
    && route.Legs is { } legs && legs.Count == legCount && legs.All(leg => leg is not null
      && double.IsFinite(leg.Miles) && leg.Miles >= 0 && double.IsFinite(leg.Seconds) && leg.Seconds >= 0
      && leg.Points is { Count: >= 2 } path && path.All(point => point is not null && point.IsValid));
}
