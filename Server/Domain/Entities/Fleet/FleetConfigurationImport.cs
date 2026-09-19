namespace Domain.Entities.Fleet;

public static class FleetConfigurationImport
{
  public static void Apply(Truck truck, string vin, bool isActive)
  {
    UpdateRevision(truck, truck.ImportedVin != vin, isActive);
    truck.ImportedVin = vin;
    truck.ImportedIsActive = isActive;
    if (truck.IsLocallyConfigured)
      return;
    truck.Vin = vin;
    truck.IsActive = isActive;
  }

  public static void Apply(Trailer trailer, string vin, bool isActive)
  {
    UpdateRevision(trailer, trailer.ImportedVin != vin, isActive);
    trailer.ImportedVin = vin;
    trailer.ImportedIsActive = isActive;
    if (trailer.IsLocallyConfigured)
      return;
    trailer.Vin = vin;
    trailer.IsActive = isActive;
  }

  public static void Apply(
    Driver driver,
    string name,
    string fuelCard,
    bool isActive
  )
  {
    UpdateRevision(
      driver,
      driver.ImportedName != name || driver.ImportedFuelCard != fuelCard,
      isActive
    );
    driver.ImportedName = name;
    driver.ImportedFuelCard = fuelCard;
    driver.ImportedIsActive = isActive;
    if (driver.IsLocallyConfigured)
      return;
    driver.Name = name;
    driver.FuelCard = fuelCard;
    driver.IsActive = isActive;
  }

  private static void UpdateRevision(
    IFleetConfiguration resource,
    bool changed,
    bool isActive
  )
  {
    if (changed || resource.ImportedIsActive != isActive)
      resource.ConfigurationRevision++;
  }
}
