using Application.Caching;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public class FleetCache(ReadCache reads)
{
  public const string CacheKey = "fleet-metadata";

  public Task<IReadOnlyList<FleetTruckInfo>> GetAsync(
    IAppDbContext dbContext,
    CancellationToken cancellationToken = default
  ) =>
    reads.GetAsync<IReadOnlyList<FleetTruckInfo>>(
      "fleet-catalog",
      CacheKey,
      async () =>
        await dbContext
          .Trucks.AsNoTracking()
          .Select(x => new FleetTruckInfo
          {
            TruckId = x.Id,
            TruckExternalId = x.ExternalId,
            UnitNumber = x.UnitNumber,
            IsActive = x.IsActive,
            DriverName = x.Driver != null ? x.Driver.Name : string.Empty,
            TrailerNumber =
              x.Trailer != null ? x.Trailer.UnitNumber : string.Empty,
            TrailerSource = x.TrailerSource,
            TrailerConflictNumber =
              x.TrailerConflict != null
                ? x.TrailerConflict.UnitNumber
                : string.Empty,
          })
          .ToListAsync(cancellationToken),
      TimeSpan.FromHours(1),
      cancellationToken
    );
}
