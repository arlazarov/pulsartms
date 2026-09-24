using System.Text.Json;
using Domain.Models.Routing;
using Domain.Rules;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

// Saved roads keep the JSON the web defaults wrote before the converter:
// the same bytes out, the same points in.
[Collection("Allocation measurements")]
[Trait("Category", "Routing")]
[Trait("Kind", "Allocation")]
public sealed class RoutePointJsonTests(ITestOutputHelper output)
{
  private static readonly JsonSerializerOptions Plain = new(
    JsonSerializerDefaults.Web
  );

  [Fact]
  public void ARoadIsWrittenAndReadAsTheDefaultsDid()
  {
    var road = Road(500);

    var written = JsonSerializer.Serialize(road, RoutingJson.Options);

    Assert.Equal(JsonSerializer.Serialize(road, Plain), written);
    var read = JsonSerializer.Deserialize<TruckRoute>(
      written,
      RoutingJson.Options
    )!;
    Assert.Equal(road.Legs[0].Points, read.Legs[0].Points);
    Assert.Equal(road.Points, read.Points);
  }

  // What the defaults accepted: names in any case, numbers as strings,
  // other properties skipped, a missing coordinate zero, null for none.
  [Fact]
  public void EveryShapeTheDefaultsReadIsReadAlike()
  {
    const string json = """
      [
        {"Latitude": 40.5, "LONGITUDE": -79.25},
        {"latitude": "41.5", "longitude": "-80", "extra": {"a": [1]}},
        {"longitude": -81},
        null
      ]
      """;

    Assert.Equal(
      JsonSerializer.Deserialize<RoutePoint?[]>(json, Plain),
      JsonSerializer.Deserialize<RoutePoint?[]>(json, RoutingJson.Options)
    );
  }

  [Fact]
  public void AnythingButACoordinateIsRefused()
  {
    Assert.Throws<JsonException>(
      () =>
        JsonSerializer.Deserialize<RoutePoint>(
          """{"latitude": true}""",
          RoutingJson.Options
        )
    );
    Assert.Throws<JsonException>(
      () =>
        JsonSerializer.Deserialize<RoutePoint>("[1, 2]", RoutingJson.Options)
    );
  }

  // Reading a saved road allocated an argument object and state per point.
  [Fact]
  public void ReadingARoadAllocatesLessThanHalfOfWhatTheDefaultsDid()
  {
    var json = JsonSerializer.Serialize(Road(20000), Plain);
    JsonSerializer.Deserialize<TruckRoute>(json, Plain);
    JsonSerializer.Deserialize<TruckRoute>(json, RoutingJson.Options);

    var before = GC.GetAllocatedBytesForCurrentThread();
    JsonSerializer.Deserialize<TruckRoute>(json, Plain);
    var plain = GC.GetAllocatedBytesForCurrentThread() - before;
    before = GC.GetAllocatedBytesForCurrentThread();
    JsonSerializer.Deserialize<TruckRoute>(json, RoutingJson.Options);
    var converted = GC.GetAllocatedBytesForCurrentThread() - before;

    output.WriteLine($"defaults {plain:N0} B, converter {converted:N0} B");
    Assert.True(converted * 2 < plain, $"{converted} of {plain}");
  }

  private static TruckRoute Road(int count)
  {
    var points = Enumerable
      .Range(0, count)
      .Select(i => new RoutePoint(40 + i * 1e-5, -80.123456789 + i * 3e-5))
      .ToList();
    return new()
    {
      Miles = 100,
      Seconds = 6000,
      Legs = [new(100, 6000, points)],
      Points = points.Take(10).ToList(),
    };
  }
}
