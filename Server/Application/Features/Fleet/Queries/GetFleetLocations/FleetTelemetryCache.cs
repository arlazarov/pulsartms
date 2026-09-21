using System.Collections.Concurrent;
using Application.Interfaces;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public sealed class FleetTelemetryCache(
  IMemoryCache cache,
  ICurrentCompany companies
) : IDisposable
{
  private readonly ConcurrentDictionary<Guid, FleetLocationsResponse> latest =
    new();
  public FleetLocationsResponse? Latest =>
    companies.Id is { } company && latest.TryGetValue(company, out var value)
      ? value
      : null;

  public const string CacheKey = "fleet-telemetry";
  private readonly SemaphoreSlim gate = new(1, 1);

  public async Task<FleetLocationsResponse> GetAsync(
    Func<CancellationToken, Task<FleetLocationsResponse>> load,
    CancellationToken cancellationToken
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (companies.Id is not { } company)
      return new();
    var key = (CacheKey, company);
    if (
      cache.TryGetValue(key, out FleetLocationsResponse? value)
      && value is not null
    )
      return value;
    await gate.WaitAsync(cancellationToken);
    try
    {
      if (cache.TryGetValue(key, out value) && value is not null)
        return value;
      value = await load(cancellationToken);
      latest[company] = value;
      cache.Set(key, value, TimeSpan.FromSeconds(10));
      return value;
    }
    finally
    {
      gate.Release();
    }
  }

  public void Dispose() => gate.Dispose();
}
