using Domain.Entities.Fleet;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class TruckSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalVehicle> trucks,
    CancellationToken cancellationToken = default
  )
  {
    var existingTrucks = await dbContext.Trucks.ToListAsync(cancellationToken);
    var existingByExternalId = existingTrucks
      .Where(x => !string.IsNullOrWhiteSpace(x.ExternalId))
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);

    foreach (var truck in trucks)
    {
      if (
        string.IsNullOrWhiteSpace(truck.UnitNumber)
        || truck.UnitNumber.Length > 10
      )
      {
        if (
          existingByExternalId.TryGetValue(
            truck.ExternalId,
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

      if (existingByExternalId.TryGetValue(truck.ExternalId, out var existing))
      {
        FleetConfigurationImport.Apply(
          existing,
          truck.IsActive ? truck.Vin : existing.ImportedVin ?? existing.Vin,
          truck.IsActive
        );

        if (truck.IsActive)
        {
          if (existing.UnitNumber != truck.UnitNumber)
            existing.ConfigurationRevision++;
          existing.UnitNumber = truck.UnitNumber;
        }

        continue;
      }

      if (!truck.IsActive)
        continue;

      dbContext.Trucks.Add(
        new Truck
        {
          Id = Guid.NewGuid(),
          ExternalId = truck.ExternalId,
          UnitNumber = truck.UnitNumber,
          Vin = truck.Vin,
          IsActive = true,
          ImportedVin = truck.Vin,
          ImportedIsActive = true,
        }
      );
    }
  }
}
