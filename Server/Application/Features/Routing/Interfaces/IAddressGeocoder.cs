using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IAddressGeocoder
{
  Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct);
  Task<ResolvedAddress> ResolveAsync(string address, CancellationToken ct);
}
