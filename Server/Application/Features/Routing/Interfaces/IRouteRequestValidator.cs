using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface IRouteRequestValidator
{
  void Validate(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile);
}
