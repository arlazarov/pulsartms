using Application.Features.Fleet.Services;
using Domain.Models.Fleet;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetLocationSnapshotTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    12,
    19,
    0,
    0,
    DateTimeKind.Utc
  );

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  public void CompleteStreamObservationKeepsAddressPositionAndTimeTogetherWithoutChangingSensors(
    int seconds
  )
  {
    var truck = Truck();
    var point = Point(truck.TruckId, Now.AddSeconds(seconds));
    FleetLocationSnapshot.UpdateFromStream([truck], [point]);
    Assert.Equal(point.FormattedLocation, truck.FormattedLocation);
    Assert.Equal(point.UpdatedAt, truck.UpdatedAt);
    Assert.Equal(point.ObservedAt, truck.ObservedAt);
    Assert.Equal(point.Latitude, truck.Latitude);
    Assert.Equal(point.Longitude, truck.Longitude);
    Assert.Equal(point.Speed, truck.Speed);
    Assert.Equal(point.Heading, truck.Heading);
    Assert.Equal("11006", truck.UnitNumber);
    Assert.Equal("Driver", truck.DriverName);
    Assert.Equal("W5631", truck.TrailerNumber);
    Assert.Equal("On", truck.EngineState);
    Assert.Equal(36, truck.FuelPercent);
    Assert.Equal(Now.AddMinutes(-2), truck.FuelUpdatedAt);
    Assert.Equal(32.4m, truck.OutsideTemperatureCelsius);
    Assert.Equal(Now.AddMinutes(-3), truck.OutsideTemperatureUpdatedAt);
  }

  [Theory]
  [InlineData("older")]
  [InlineData("other truck")]
  [InlineData("undated")]
  public void UnusableStreamCannotBorrowAnAddressFromAnotherTimeOrTruck(
    string reason
  )
  {
    var truck = Truck();
    var point = Point(truck.TruckId, Now.AddSeconds(5));
    if (reason == "older")
      point.UpdatedAt = Now.AddSeconds(-1);
    if (reason == "other truck")
      point.TruckId = Guid.NewGuid();
    if (reason == "undated")
      point.UpdatedAt = default;
    var points = new List<TruckLocation> { point };
    FleetLocationSnapshot.UpdateFromStream([truck], points);
    Assert.Equal("I 40, Los Pinos, NM", truck.FormattedLocation);
    Assert.Equal(Now, truck.UpdatedAt);
    Assert.Equal(35, truck.Latitude);
    Assert.Equal(60, truck.Speed);
  }

  [Theory]
  [InlineData("")]
  [InlineData(" ")]
  public void MissingAddressDoesNotDiscardNewerGpsOrReuseOldAddress(
    string address
  )
  {
    var truck = Truck();
    var earlier = Point(truck.TruckId, Now.AddSeconds(5));
    var latest = Point(truck.TruckId, Now.AddSeconds(10));
    latest.FormattedLocation = address;
    FleetLocationSnapshot.UpdateFromStream([truck], [latest, earlier]);
    Assert.Equal(latest.UpdatedAt, truck.UpdatedAt);
    Assert.Equal(latest.ObservedAt, truck.ObservedAt);
    Assert.Equal(latest.Latitude, truck.Latitude);
    Assert.Equal(latest.Longitude, truck.Longitude);
    Assert.Equal(latest.Speed, truck.Speed);
    Assert.Equal(string.Empty, truck.FormattedLocation);
    Assert.Equal(36, truck.FuelPercent);
  }

  [Fact]
  public void RetainedPointCannotAgeAnEquallyDatedFreshObservation()
  {
    var truck = Truck();
    truck.ObservedAt = Now.AddMinutes(1);
    var retained = Point(truck.TruckId, Now);
    FleetLocationSnapshot.UpdateFromStream([truck], [retained]);
    Assert.Equal(Now.AddMinutes(1), truck.ObservedAt);
    Assert.Equal(35, truck.Latitude);
  }

  [Fact]
  public void UnorderedStreamUsesLatestObservationWithoutSharingPointObjects()
  {
    var truck = Truck();
    var latest = Point(truck.TruckId, Now.AddSeconds(10));
    var previous = Point(truck.TruckId, Now.AddSeconds(5));
    previous.FormattedLocation = "Previous town";
    FleetLocationSnapshot.UpdateFromStream([truck], [latest, previous]);
    latest.FormattedLocation = "Changed externally";
    Assert.Equal("I 40, Los Pinos, NM 87026, US", truck.FormattedLocation);
    Assert.Equal(Now.AddSeconds(10), truck.UpdatedAt);
  }

  private static TruckLocation Truck() =>
    new()
    {
      TruckId = Guid.NewGuid(),
      UnitNumber = "11006",
      DriverName = "Driver",
      TrailerNumber = "W5631",
      Latitude = 35,
      Longitude = -106,
      Speed = 60,
      UpdatedAt = Now,
      FormattedLocation = "I 40, Los Pinos, NM",
      EngineState = "On",
      FuelPercent = 36,
      FuelUpdatedAt = Now.AddMinutes(-2),
      OutsideTemperatureCelsius = 32.4m,
      OutsideTemperatureUpdatedAt = Now.AddMinutes(-3),
    };

  private static TruckLocation Point(Guid truckId, DateTime at) =>
    new()
    {
      TruckId = truckId,
      UpdatedAt = at,
      ObservedAt = Now.AddSeconds(20),
      Latitude = 35.1m,
      Longitude = -106.1m,
      Speed = 62,
      Heading = 90,
      FormattedLocation = "I 40, Los Pinos, NM 87026, US",
    };
}
