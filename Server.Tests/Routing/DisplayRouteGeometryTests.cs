using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public class DisplayRouteGeometryTests
{
  [Theory]
  [InlineData(true, 3, true)]
  [InlineData(true, 2, false)]
  [InlineData(false, 3, false)]
  public void DisplayOmitsOnlyGeometryForTheExactKnownPlan(
    bool sameId,
    int version,
    bool omitted
  )
  {
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      Version = 3,
      Route = new()
      {
        Miles = 10,
        Legs = [new(10, 600, [new(40, -80), new(41, -80)])],
      },
      ReferenceRoute = new()
      {
        Legs = [new(20, 1200, [new(39, -80), new(41, -80)])],
      },
      Stops = [new(Guid.NewGuid(), "Stop", "Address", 1, new(41, -80))],
    };
    PlanningReadService.TrimForDisplay(
      plan,
      sameId ? plan.Id : Guid.NewGuid(),
      version
    );
    Assert.Equal(omitted, plan.GeometryOmitted);
    Assert.Equal(omitted ? 0 : 2, plan.Route.Legs[0].Points.Count);
    Assert.Equal(omitted ? 0 : 2, plan.ReferenceRoute.Legs[0].Points.Count);
    Assert.Equal(10, plan.Route.Miles);
    Assert.Single(plan.Stops);
  }

  [Fact]
  public void RemovesStraightLinePointsWithoutChangingSource()
  {
    var points = Enumerable
      .Range(0, 1000)
      .Select(i => new RoutePoint(40, -80 + i * .00001))
      .ToList();
    var result = DisplayRouteGeometry.Simplify(points);
    Assert.Equal(2, result.Count);
    Assert.Equal(points[0], result[0]);
    Assert.Equal(points[^1], result[^1]);
    Assert.Equal(1000, points.Count);
  }

  [Fact]
  public void KeepsSmallRoadCurvesVisibleAtStreetZoom()
  {
    // A six-metre bend must not become a straight line beside the road.
    var points = new List<RoutePoint>
    {
      new(40, -80),
      new(40 + 6d / 111320, -79.999),
      new(40, -79.998),
    };
    Assert.Equal(points, DisplayRouteGeometry.Simplify(points));
    Assert.Equal(2, DisplayRouteGeometry.Simplify(points, 10).Count);
  }

  [Fact]
  public void KeepsRoadTurnsAndRepeatedEndpoints()
  {
    var points = new List<RoutePoint>
    {
      new(40, -80),
      new(40, -79.99),
      new(40.01, -79.99),
      new(40, -80),
    };
    Assert.Equal(points, DisplayRouteGeometry.Simplify(points));
  }
}
