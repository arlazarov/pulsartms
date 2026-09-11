using Application.Features.Fleet.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public sealed class FleetTelemetryCache(IMemoryCache cache) : IDisposable
{
  private FleetLocationsResponse? latest;
  public FleetLocationsResponse? Latest => Volatile.Read(ref latest);

  public const string CacheKey = "fleet-telemetry";
  private readonly SemaphoreSlim gate = new(1, 1);

  public async Task<FleetLocationsResponse> GetAsync(
    Func<CancellationToken, Task<FleetLocationsResponse>> load,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (cache.TryGetValue(CacheKey, out FleetLocationsResponse? value) && value is not null)
      return value;
    await gate.WaitAsync(cancellationToken);
    try
    {
      if (cache.TryGetValue(CacheKey, out value) && value is not null)
        return value;
      value = await load(cancellationToken);
      Volatile.Write(ref latest, value);
      cache.Set(CacheKey, value, TimeSpan.FromSeconds(10));
      return value;
    }
    finally { gate.Release(); }
  }

  public void Dispose() => gate.Dispose();
}
