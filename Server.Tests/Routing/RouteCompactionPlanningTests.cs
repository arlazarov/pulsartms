using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteCompactionPlanningTests
{
  [Fact]
  public void StraightCorridorNeedsOnlyEndpointsForDisplay()
  {
    var points = Enumerable
      .Range(0, 20_001)
      .Select(i => new RoutePoint(40, -100 + i * .0004))
      .ToList();
    var simplified = DisplayRouteGeometry.Simplify(points);
    Assert.Equal(new[] { points[0], points[^1] }, simplified);
    Assert.Equal(20_001, points.Count);
  }

  [Fact]
  public void DisplayCompactionPreservesLegSummariesAndStopBoundaries()
  {
    var a = new RoutePoint(40, -80);
    var b = new RoutePoint(40, -79);
    var c = new RoutePoint(41, -79);
    var plan = new RoutePlan
    {
      Route = new()
      {
        Miles = 300,
        Seconds = 19000,
        Legs =
        [
          new(100, 7000, [a, new(40, -79.5), b]),
          new(200, 12000, [b, new(40.5, -79), c]),
        ],
      },
      Stops =
      [
        new(Guid.NewGuid(), "Pickup", "A", 1, a),
        new(Guid.NewGuid(), "Yard", "B", 2, b),
        new(Guid.NewGuid(), "Delivery", "C", 3, c),
      ],
    };
    var ids = plan.Stops.Select(x => x.Id).ToArray();
    PlanningReadService.TrimForDisplay(plan);
    Assert.Equal(300, plan.Route.Miles);
    Assert.Equal(19000, plan.Route.Seconds);
    Assert.Equal(new[] { 100d, 200d }, plan.Route.Legs.Select(x => x.Miles));
    Assert.Equal(
      new[] { 7000d, 12000d },
      plan.Route.Legs.Select(x => x.Seconds)
    );
    Assert.Equal(ids, plan.Stops.Select(x => x.Id));
    Assert.Equal(new[] { a, b }, plan.Route.Legs[0].Points);
    Assert.Equal(new[] { b, c }, plan.Route.Legs[1].Points);
  }

  [Fact]
  public void PreservingTotalMilesDoesNotPreserveIntermediateProgress()
  {
    // Small bends in only the first half change geometric proportions.
    var points = Enumerable
      .Range(0, 1001)
      .Select(i => new RoutePoint(
        40 + (i > 0 && i < 500 ? (i % 2 == 0 ? 4 : -4) : 0) / 111320d,
        -80 + i / (111320d * Math.Cos(40 * Math.PI / 180))
      ))
      .ToList();
    var compact = DisplayRouteGeometry.Simplify(points, 10);
    Assert.Equal(2, compact.Count);
    var original = new RouteGeometry(new() { Legs = [new(10, 600, points)] });
    var simplified = new RouteGeometry(
      new() { Legs = [new(10, 600, compact)] }
    );
    Assert.Equal(original.Miles, simplified.Miles, 8);
    var originalProgress = original.Match(points[500]).Along;
    var simplifiedProgress = simplified.Match(points[500]).Along;
    Assert.True(originalProgress - simplifiedProgress > 2);
  }
}
