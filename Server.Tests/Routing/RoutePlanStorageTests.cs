using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public class RoutePlanStorageTests
{
  [Fact]
  public void RoutePointSerializesCoordinatesButRecomputesValidationForLegacyJson()
  {
    var json = JsonSerializer.Serialize(
      new RoutePoint(40, -80),
      RoutingJson.Options
    );
    Assert.DoesNotContain("isValid", json);
    var legacy = JsonSerializer.Deserialize<RoutePoint>(
      "{\"latitude\":40,\"longitude\":-80,\"isValid\":false}",
      RoutingJson.Options
    )!;
    Assert.True(legacy.IsValid);
  }

  [Fact]
  public void StorageOmitsOnlyDuplicateAggregatePointsWithoutMutatingOriginal()
  {
    var points = Enumerable
      .Range(0, 1000)
      .Select(i => new RoutePoint(40, -80 + i / 1000d))
      .ToList();
    var route = new TruckRoute
    {
      Points = points,
      Legs = [new(100, 6000, points)],
      Miles = 100,
      Seconds = 6000,
    };
    var plan = new RoutePlan { Route = route, ReferenceRoute = route };
    var old = JsonSerializer.Serialize(plan, RoutingJson.Options);
    var stored = RoutePlanStorage.Serialize(plan);
    var restored = JsonSerializer.Deserialize<RoutePlan>(
      stored,
      RoutingJson.Options
    )!;
    Assert.True(stored.Length < old.Length * .6);
    Assert.Empty(restored.Route.Points);
    Assert.Empty(restored.ReferenceRoute!.Points);
    Assert.Equal(points, restored.Route.Legs[0].Points);
    Assert.Equal(points, restored.ReferenceRoute.Legs[0].Points);
    Assert.Equal(6000, restored.Route.Seconds);
    Assert.Same(points, plan.Route.Points);
    Assert.Equal(
      points,
      JsonSerializer
        .Deserialize<RoutePlan>(old, RoutingJson.Options)!
        .Route.Points
    );
  }

  [Fact]
  public void StandaloneBaseAndDeadheadStoragePreservesExactLegsWithoutDuplicateAggregateCoordinates()
  {
    var points = Enumerable
      .Range(0, 1000)
      .Select(index => new RoutePoint(40, -80 + index / 1000d))
      .ToList();
    var route = new TruckRoute
    {
      Points = points,
      Legs = [new(100, 6000, points)],
      Miles = 100,
      Seconds = 6000,
      Warnings = ["Access warning"],
    };
    var stored = RoutePlanStorage.Serialize(route);
    var restored = SavedRouteReader.Route(stored, 1)!;
    using var json = JsonDocument.Parse(stored);
    Assert.False(json.RootElement.TryGetProperty("points", out _));
    Assert.Empty(restored.Points);
    Assert.Equal(points, restored.Legs[0].Points);
    Assert.Equal(route.Miles, restored.Miles);
    Assert.Equal(route.Seconds, restored.Seconds);
    Assert.Equal(route.Warnings, restored.Warnings);
    Assert.Same(points, route.Points);
  }
}
