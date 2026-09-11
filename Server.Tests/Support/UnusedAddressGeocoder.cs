using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;

namespace Server.Tests.Support;

internal sealed class UnusedAddressGeocoder : IAddressGeocoder
{
  public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new InvalidOperationException("Unexpected address lookup.");
  public Task<ResolvedAddress> ResolveAsync(string address, CancellationToken ct) => throw new InvalidOperationException("Unexpected address lookup.");
}
