using Application.Features.Routing.Models;

namespace Server.Tests.Support;

public static class FuelGeometryFixture
{
  public static TruckRoute RoundTrip(int pointsPerLeg = 2001, int legCount = 4)
  {
    var route = new TruckRoute
    {
      Miles = legCount * 1500,
      Seconds = legCount * 90_123,
      CalculatedAt = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
      Warnings = ["Checked access warning"],
    };
    for (var leg = 0; leg < legCount; leg++)
    {
      var points = new List<RoutePoint>(pointsPerLeg);
      for (var i = 0; i < pointsPerLeg; i++)
      {
        var t = i / (pointsPerLeg - 1d);
        if (leg % 2 == 1)
          t = 1 - t;
        points.Add(new(40 + Math.Sin(t * Math.PI * 16) * .1, -105 + 20 * t));
      }
      route.Legs.Add(new(1500, 90_123, points));
      route.Points.AddRange(leg == 0 ? points : points.Skip(1));
    }
    return route;
  }
}
