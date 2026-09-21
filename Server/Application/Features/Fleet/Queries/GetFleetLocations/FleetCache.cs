using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public class FleetCache(IMemoryCache cache)
{
  public const string CacheKey = "fleet-metadata";
  private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

  public async Task<IReadOnlyList<FleetTruckInfo>> GetAsync(
    IAppDbContext dbContext,
    CancellationToken cancellationToken = default
  )
  {
    if (
      cache.TryGetValue(CacheKey, out IReadOnlyList<FleetTruckInfo>? cached)
      && cached is not null
    )
    {
      return cached;
    }

    var fleet = await dbContext
      .Trucks.AsNoTracking()
      .Select(x => new FleetTruckInfo
      {
        TruckId = x.Id,
        TruckExternalId = x.ExternalId,
        UnitNumber = x.UnitNumber,
        IsActive = x.IsActive,
        DriverName = x.Driver != null ? x.Driver.Name : string.Empty,
        TrailerNumber = x.Trailer != null ? x.Trailer.UnitNumber : string.Empty,
      })
      .ToListAsync(cancellationToken);

    cache.Set(CacheKey, fleet, CacheDuration);

    return fleet;
  }
}
