using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.Routes;

public sealed class RouteRequestValidator : IRouteRequestValidator
{
  public void Validate(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile
  )
  {
    if (profile.Validate() is { } error)
      throw new RoutePlanningException(error);
    if (points.Count is < 2 or > 50 || points.Any(x => !x.IsValid))
      throw new RoutePlanningException("A route needs 2–50 valid locations.");
  }
}
