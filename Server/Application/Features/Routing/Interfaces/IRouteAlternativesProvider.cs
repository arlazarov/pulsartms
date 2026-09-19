using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface IRouteAlternativesProvider
{
  Task<IReadOnlyList<TruckRoute>> CalculateAlternativesAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  );
}
