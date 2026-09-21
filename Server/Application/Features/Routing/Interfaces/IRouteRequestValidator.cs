using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IRouteRequestValidator
{
  void Validate(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile);
}
