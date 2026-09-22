using Domain.Models.Routing;
using Domain.Rules.Routing;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class SharedRouteGeometryTests
{
  [Fact]
  public void CachedGeometryIsIndependentOfMutableSourceLists()
  {
    var route = FuelGeometryFixture.RoundTrip(1001, 2);
    var expected = new ReferenceRouteGeometry(route);
    var actual = new RouteGeometry(route);
    route.Legs[0].Points.Clear();
    route.Legs.Clear();
    route.Points.Clear();
    for (var mile = 0; mile <= 3000; mile += 100)
    {
      var point = expected.At(mile);
      Assert.True(RouteGeometry.Distance(point, actual.At(mile)) < 1e-7);
      Equal(expected.Match(point), actual.Match(point));
    }
  }

  [Theory]
  [InlineData(2)]
  [InlineData(1001)]
  [InlineData(10001)]
  public void SharedIndexPreservesOriginalProgressOnCurvesAndRepeatedRoads(
    int pointsPerLeg
  )
  {
    var route = FuelGeometryFixture.RoundTrip(pointsPerLeg, 4);
    var expected = new ReferenceRouteGeometry(route);
    var actual = new RouteGeometry(route);
    var random = new Random(321);
    for (var i = 0; i < 100; i++)
    {
      var mile = random.NextDouble() * expected.Miles;
      var point = expected.At(mile) with
      {
        Latitude = expected.At(mile).Latitude + .002,
      };
      Equal(
        expected.Match(point, i % 4 * 1500),
        actual.Match(point, i % 4 * 1500)
      );
    }
  }

  [Fact]
  public void OriginalProgressSurvivesUnevenSmallBends()
  {
    var points = Enumerable
      .Range(0, 1001)
      .Select(i => new RoutePoint(
        40 + (i > 0 && i < 500 ? (i % 2 == 0 ? 4 : -4) : 0) / 111320d,
        -80 + i / (111320d * Math.Cos(40 * Math.PI / 180))
      ))
      .ToList();
    var route = new TruckRoute { Legs = [new(10, 600, points)] };
    var expected = new ReferenceRouteGeometry(route);
    var actual = new RouteGeometry(route);
    for (var i = 0; i < points.Count; i += 10)
      Equal(expected.Match(points[i]), actual.Match(points[i]));
    Assert.Equal(expected.Miles, actual.Miles, 8);
  }

  private static void Equal(
    (double Along, double Away, RoutePoint Point) expected,
    (double Along, double Away, RoutePoint Point) actual
  )
  {
    Assert.Equal(expected.Along, actual.Along, 6);
    Assert.Equal(expected.Away, actual.Away, 8);
    Assert.True(RouteGeometry.Distance(expected.Point, actual.Point) < 1e-7);
  }
}
