using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteChoiceDisplayTests(ITestOutputHelper output)
{
  [Fact]
  public void DisplayRetainsMandatoryAnchorsMetricsAndRoadBendsWithoutChangingExactDraft()
  {
    var bend = new RoutePoint(40 + 6d / 111320, -79.999);
    var first = new RoutePoint(40, -80);
    var middle = new RoutePoint(40, -79.998);
    var route = RouteViaGeometry.Join(
      [new(10, 60, [first, bend, middle]), new(12, 70, [middle, first])],
      ["Access warning"],
      DateTime.UtcNow
    );
    var preview = Preview(route);
    var json = JsonSerializer.Serialize(preview, RoutingJson.Options);
    var display = RouteChoiceDisplay.Create(preview);

    Assert.Equal(route.CalculatedAt, display.Options[0].Route.CalculatedAt);
    Assert.Equal(route.Miles, display.Options[0].Route.Miles);
    Assert.Equal(route.Seconds, display.Options[0].Route.Seconds);
    Assert.Equal(route.Warnings, display.Options[0].Route.Warnings);
    Assert.Equal(route.Legs[0].Points, display.Options[0].Route.Legs[0].Points);
    Assert.Equal(route.Legs[1].Points, display.Options[0].Route.Legs[1].Points);
    Assert.Equal(preview.Stops, display.Stops);
    Assert.Equal(preview.ViaPoints, display.ViaPoints);
    Assert.Equal(
      preview.Options[0].DifferenceMiles,
      display.Options[0].DifferenceMiles
    );
    display.Options[0].Route.Legs[0].Points.Clear();
    display.Options[0].Route.Warnings.Clear();
    Assert.Equal(json, JsonSerializer.Serialize(preview, RoutingJson.Options));
  }

  [Fact]
  public void LongStraightPreviewTransfersLegGeometryOnceAndOnlySavedComparisonMetrics()
  {
    var points = Enumerable
      .Range(0, 37_000)
      .Select(i => new RoutePoint(40 + 3d * i / 36_999, -80))
      .ToList();
    var route = RouteViaGeometry.Join(
      [new(2800, 150000, points)],
      [],
      DateTime.UtcNow
    );
    var preview = Preview(route);
    var original = JsonSerializer.SerializeToUtf8Bytes(
      preview,
      RoutingJson.Options
    );
    var projected = RouteChoiceDisplay.Create(preview);
    var compact = JsonSerializer.SerializeToUtf8Bytes(
      projected,
      RoutingJson.Options
    );
    using var json = JsonDocument.Parse(compact);
    Assert.False(
      json.RootElement.GetProperty("savedRoute").TryGetProperty("legs", out _)
    );
    Assert.False(
      json.RootElement.GetProperty("savedRoute").TryGetProperty("points", out _)
    );
    foreach (
      var option in json.RootElement.GetProperty("options").EnumerateArray()
    )
      Assert.False(option.GetProperty("route").TryGetProperty("points", out _));
    Assert.All(
      projected.Options,
      option =>
        Assert.Equal(
          new[] { points[0], points[^1] },
          option.Route.Legs[0].Points
        )
    );
    Assert.Equal(37_000, route.Legs[0].Points.Count);
    Assert.True(compact.Length < original.Length / 100);
    output.WriteLine(
      $"Deterministic straight-road fixture: 3 alternatives + saved baseline, 37,000 points each; original JSON={original.Length:N0} B; display JSON={compact.Length:N0} B. Byte counts are not production latency or RSS."
    );
  }

  private static RouteChoicePreview Preview(TruckRoute route) =>
    new(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      1383,
      4,
      DateTime.UtcNow.AddMinutes(10),
      [
        new(Guid.NewGuid(), "Pickup", "", 1, route.Legs[0].Points[0]),
        new(Guid.NewGuid(), "Delivery", "", 2, route.Legs[^1].Points[^1]),
      ],
      [],
      Enumerable
        .Range(1, 3)
        .Select(i => new RouteChoiceOption(i, route, i, i * 10))
        .ToList(),
      route
    );
}
