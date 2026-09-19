using Application.Features.Dispatch.Commands.SyncDispatche;
using Domain.Entities.Fleet;
using Xunit;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetConfigurationImportTests
{
  [Fact]
  public void TruckImportRetainsLocalConfigurationAndRefreshesSource()
  {
    var truck = new Truck
    {
      ExternalId = "provider-truck",
      Vin = "LOCALVIN",
      IsActive = false,
      IsLocallyConfigured = true,
      ImportedVin = "OLDVIN",
      ImportedIsActive = true,
      ConfigurationRevision = 4,
    };

    FleetConfigurationImport.Apply(truck, "NEWVIN", true);

    Assert.Equal("LOCALVIN", truck.Vin);
    Assert.False(truck.IsActive);
    Assert.Equal("NEWVIN", truck.ImportedVin);
    Assert.True(truck.ImportedIsActive);
    Assert.Equal(5, truck.ConfigurationRevision);
    Assert.Equal("provider-truck", truck.ExternalId);
  }

  [Fact]
  public void UnchangedImportDoesNotAdvanceConfigurationRevision()
  {
    var trailer = new Trailer
    {
      Vin = "VIN",
      ImportedVin = "VIN",
      IsActive = true,
      ImportedIsActive = true,
      ConfigurationRevision = 2,
    };

    FleetConfigurationImport.Apply(trailer, "VIN", true);

    Assert.Equal(2, trailer.ConfigurationRevision);
  }

  [Fact]
  public void DriverImportDoesNotMoveLocalNameOrFuelCardToAnotherIdentity()
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "eld-driver",
      Name = "Local display name",
      FuelCard = "LOCAL",
      IsActive = true,
      IsLocallyConfigured = true,
    };

    FleetConfigurationImport.Apply(driver, "Imported Driver", "SOURCE", true);

    Assert.Equal("Local display name", driver.Name);
    Assert.Equal("LOCAL", driver.FuelCard);
    Assert.Equal("Imported Driver", driver.ImportedName);
    Assert.Equal("SOURCE", driver.ImportedFuelCard);
    Assert.Equal("eld-driver", driver.ExternalId);
    Assert.Same(driver, DriverMatcher.Match([driver], "Imported Driver"));
    Assert.Same(
      driver,
      new DriverMatcher.Index([driver]).Match("Imported Driver")
    );
  }

  [Fact]
  public void ImportedConfigurationRemainsEffectiveWithoutLocalOverride()
  {
    var driver = new Driver { Name = "Old", FuelCard = "Old" };

    FleetConfigurationImport.Apply(driver, "Updated", "New", true);

    Assert.Equal("Updated", driver.Name);
    Assert.Equal("New", driver.FuelCard);
    Assert.True(driver.IsActive);
  }
}
