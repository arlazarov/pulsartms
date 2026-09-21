using Domain.Entities.Fleet;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class TrailerSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalTrailer> trailers,
    CancellationToken cancellationToken = default
  )
  {
    var existingTrailers = await dbContext.Trailers.ToListAsync(
      cancellationToken
    );
    var existingByExternalId = existingTrailers
      .Where(x => !string.IsNullOrWhiteSpace(x.ExternalId))
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);

    foreach (var trailer in trailers)
    {
      if (
        string.IsNullOrWhiteSpace(trailer.UnitNumber)
        || trailer.UnitNumber.Length > 10
      )
      {
        if (
          existingByExternalId.TryGetValue(
            trailer.ExternalId,
            out var invalidExisting
          )
        )
          FleetConfigurationImport.Apply(
            invalidExisting,
            invalidExisting.ImportedVin ?? invalidExisting.Vin,
            false
          );

        continue;
      }

      if (
        existingByExternalId.TryGetValue(trailer.ExternalId, out var existing)
      )
      {
        if (existing.UnitNumber != trailer.UnitNumber)
          existing.ConfigurationRevision++;
        existing.UnitNumber = trailer.UnitNumber;
        FleetConfigurationImport.Apply(existing, trailer.Vin, trailer.IsActive);
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
          ImportedVin = trailer.Vin,
          ImportedIsActive = trailer.IsActive,
        }
      );
    }
  }
}
