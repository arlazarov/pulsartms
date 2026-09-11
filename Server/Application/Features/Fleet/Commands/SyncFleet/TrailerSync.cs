using Application.Features.Fleet.Models;
using Domain.Entities.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class TrailerSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalTrailer> trailers,
    CancellationToken cancellationToken = default
  )
  {
    var existingTrailers = await dbContext.Trailers.ToListAsync(cancellationToken);
    var existingByExternalId = existingTrailers
      .Where(x => !string.IsNullOrWhiteSpace(x.ExternalId))
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);

    foreach (var trailer in trailers)
    {
      if (string.IsNullOrWhiteSpace(trailer.UnitNumber) || trailer.UnitNumber.Length > 10)
      {
        if (existingByExternalId.TryGetValue(trailer.ExternalId, out var invalidExisting))
          invalidExisting.IsActive = false;

        continue;
      }

      if (existingByExternalId.TryGetValue(trailer.ExternalId, out var existing))
      {
        existing.UnitNumber = trailer.UnitNumber;
        existing.Vin = trailer.Vin;
        existing.IsActive = trailer.IsActive;
        continue;
      }

      dbContext.Trailers.Add(
        new Trailer
        {
          Id = Guid.NewGuid(),
          ExternalId = trailer.ExternalId,
          UnitNumber = trailer.UnitNumber,
          Vin = trailer.Vin,
          IsActive = trailer.IsActive,
        }
      );
    }
  }
}
