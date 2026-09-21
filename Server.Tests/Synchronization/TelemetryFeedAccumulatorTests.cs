using System.Text.Json;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Services;
using Domain.Models.Fleet;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class TelemetryFeedAccumulatorTests
{
  [Fact]
  public void CompactPagesKeepIndependentLatestMeasurementsAndOnlyRecentDisplayPoints()
  {
    var now = DateTime.UtcNow;
    var batch = new TelemetryFeedAccumulator(now);
    batch.Append(
      new(
        [
          new(
            "truck",
            new()
            {
              ExternalId = "truck",
              UpdatedAt = now,
              Latitude = 40,
            },
            "On",
            now,
            50,
            now
          ),
        ],
        "one",
        true
      )
    );
    batch.Append(
      new(
        [
          new(
            "truck",
            new()
            {
              ExternalId = "truck",
              UpdatedAt = now.AddMinutes(-3),
              Latitude = 20,
            },
            "Off",
            now.AddSeconds(-1),
            60,
            now.AddSeconds(1)
          ),
        ],
        "two",
        false
      )
    );
    var state = new SynchronizationState();
    state.Apply(batch.Updates, "two");
    Assert.Single(batch.Updates);
    Assert.Single(batch.Points);
    var vehicle = state.Vehicles["truck"];
    Assert.Equal(40, vehicle.Latitude);
    Assert.Equal("On", vehicle.EngineState);
    Assert.Equal(60, vehicle.FuelPercent);
    Assert.Equal("two", state.TelemetryCursor);
  }

  [Fact]
  public void OutsideTemperatureKeepsItsOwnTimestampAcrossPagesAndCheckpointRestoration()
  {
    var now = DateTime.UtcNow;
    var batch = new TelemetryFeedAccumulator(now);
    batch.Append(
      new(
        [new("truck", null, null, null, null, null, 0, now.AddMinutes(-1))],
        "one",
        true
      )
    );
    batch.Append(
      new(
        [
          new(
            "truck",
            new() { UpdatedAt = now },
            null,
            null,
            null,
            null,
            20,
            now.AddMinutes(-2)
          ),
        ],
        "two",
        true
      )
    );
    batch.Append(
      new([new("truck", null, null, null, null, null)], "three", false)
    );
    var state = new SynchronizationState();
    state.Apply(batch.Updates, "three");
    var restored = JsonSerializer.Deserialize<SynchronizationState>(
      JsonSerializer.Serialize(state)
    )!;
    restored.Apply(
      [new("truck", null, null, null, null, null, 12, now.AddMinutes(-3))],
      "four"
    );
    Assert.Equal(0, restored.Vehicles["truck"].OutsideTemperatureCelsius);
    Assert.Equal(
      now.AddMinutes(-1),
      restored.Vehicles["truck"].OutsideTemperatureUpdatedAt
    );
    Assert.Equal(now, restored.Vehicles["truck"].UpdatedAt);
  }

  [Fact]
  public void OversizedPagesFailBeforeTheyCanReplaceTheSavedCursor()
  {
    var now = DateTime.UtcNow;
    var batch = new TelemetryFeedAccumulator(now);
    var state = new SynchronizationState { TelemetryCursor = "saved" };
    var point = new VehicleLocationPoint
    {
      ExternalId = "truck",
      UpdatedAt = now,
      FormattedLocation = new string('x', 8 * 1024 * 1024),
    };
    Assert.Throws<InvalidOperationException>(
      () =>
        batch.Append(
          new([new("truck", point, null, null, null, null)], "oversized", false)
        )
    );
    Assert.Equal("saved", state.TelemetryCursor);
    Assert.Empty(state.Vehicles);
  }

  [Fact]
  public void EndlessDistinctPagesAreBounded()
  {
    var batch = new TelemetryFeedAccumulator(DateTime.UtcNow);
    for (var page = 0; page < 256; page++)
      batch.Append(new([], page.ToString(), true));
    Assert.Throws<InvalidOperationException>(
      () => batch.Append(new([], "overflow", true))
    );
  }
}
