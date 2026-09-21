using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IRouteAlternativesProvider
{
  Task<IReadOnlyList<TruckRoute>> CalculateAlternativesAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  );
}
