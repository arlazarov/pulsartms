using Application.Features.Fleet.Models;
using Domain.Entities.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class DriverSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalDriver> drivers,
    CancellationToken cancellationToken = default
  )
  {
    var existingDrivers = await dbContext.Drivers.ToListAsync(
      cancellationToken
    );

    var existingByExternalId = existingDrivers
      .Where(x => !string.IsNullOrWhiteSpace(x.ExternalId))
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);

    foreach (var driver in drivers)
    {
      if (existingByExternalId.TryGetValue(driver.ExternalId, out var existing))
      {
        FleetConfigurationImport.Apply(
          existing,
          driver.Name,
          driver.FuelCard,
          driver.IsActive
        );
        continue;
      }

      dbContext.Drivers.Add(
        new Driver
        {
          Id = Guid.NewGuid(),
          ExternalId = driver.ExternalId,
          Name = driver.Name,
          FuelCard = driver.FuelCard,
          IsActive = driver.IsActive,
          ImportedName = driver.Name,
          ImportedFuelCard = driver.FuelCard,
          ImportedIsActive = driver.IsActive,
        }
      );
    }
  }
}
