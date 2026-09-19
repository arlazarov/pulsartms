using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface IRoutingProvider
{
  bool IsConfigured { get; }
  Task<TruckRoute> CalculateAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  );
  Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct);
}
