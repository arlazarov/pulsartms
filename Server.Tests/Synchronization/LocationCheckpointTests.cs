using System.Text.Json;
using Application.Features.Fleet.Models;
using Application.Features.Synchronization.Models;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class LocationCheckpointTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    13,
    18,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public void StreamLocationSurvivesCheckpointWithoutAdvancingFeedCursor()
  {
    var state = State();
    state.ApplyLocations([Point(Now)]);
    var restored = JsonSerializer.Deserialize<SynchronizationState>(
      JsonSerializer.Serialize(state)
    )!;
    var vehicle = restored.Vehicles["truck"];
    Assert.Equal(Now, vehicle.UpdatedAt);
    Assert.Equal(35.5m, vehicle.Latitude);
    Assert.Equal(-99m, vehicle.Longitude);
    Assert.Equal(60m, vehicle.Speed);
    Assert.Equal("Current address", vehicle.FormattedLocation);
    Assert.Equal("feed-cursor", restored.TelemetryCursor);
    Assert.Equal(81m, vehicle.FuelPercent);
    Assert.Equal("On", vehicle.EngineState);
  }

  [Fact]
  public void OlderFeedGpsCannotOverwriteStreamButSensorsStillAdvance()
  {
    var state = State();
    state.ApplyLocations([Point(Now)]);
    state.Apply(
      [new("truck", Point(Now.AddHours(-1)), "Idle", Now, 80, Now)],
      "next-cursor"
    );
    Assert.Equal(Now, state.Vehicles["truck"].UpdatedAt);
    Assert.Equal("Idle", state.Vehicles["truck"].EngineState);
    Assert.Equal(80m, state.Vehicles["truck"].FuelPercent);
    Assert.Equal("next-cursor", state.TelemetryCursor);
  }

  [Fact]
  public void StreamCanIntroduceVehicleAndClearAnUnavailableAddress()
  {
    var state = new SynchronizationState();
    state.ApplyLocations([Point(Now)]);
    var next = Point(Now.AddSeconds(5));
    next.FormattedLocation = "";
    state.ApplyLocations([next, Point(Now)]);
    Assert.Equal(next.UpdatedAt, state.Vehicles["truck"].UpdatedAt);
    Assert.Equal("", state.Vehicles["truck"].FormattedLocation);
    Assert.Null(state.TelemetryCursor);
  }

  private static SynchronizationState State()
  {
    var state = new SynchronizationState();
    state.Apply(
      [
        new(
          "truck",
          Point(Now.AddHours(-2)),
          "On",
          Now.AddHours(-2),
          81,
          Now.AddHours(-2)
        ),
      ],
      "feed-cursor"
    );
    return state;
  }

  private static VehicleLocationPoint Point(DateTime at) =>
    new()
    {
      ExternalId = "truck",
      Latitude = 35.5m,
      Longitude = -99m,
      Speed = 60,
      UpdatedAt = at,
      FormattedLocation = "Current address",
    };
}
