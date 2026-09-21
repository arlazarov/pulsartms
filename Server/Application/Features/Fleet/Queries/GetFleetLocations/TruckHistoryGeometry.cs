using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public static class TruckHistoryGeometry
{
  public static IReadOnlyList<VehicleLocationPoint> Simplify(
    IReadOnlyList<VehicleLocationPoint> points
  )
  {
    var result = new List<VehicleLocationPoint>();
    var start = 0;
    while (start < points.Count)
    {
      var end = start;
      // Preserve temporal anchors so simplification cannot turn a continuous
      // GPS trace into a gap, or connect across missing telemetry.
      while (
        end + 1 < points.Count
        && points[end + 1].UpdatedAt - points[end].UpdatedAt
          <= TimeSpan.FromMinutes(5)
        && points[end + 1].UpdatedAt - points[start].UpdatedAt
          <= TimeSpan.FromMinutes(2)
      )
        end++;
      var geometry = Enumerable
        .Range(start, end - start + 1)
        .Select(i => new RoutePoint(
          (double)points[i].Latitude,
          (double)points[i].Longitude
        ))
        .ToList();
      var kept = DisplayRouteGeometry.Simplify(geometry, 20);
      var cursor = 0;
      foreach (var point in kept)
      {
        while (
          cursor < geometry.Count && !ReferenceEquals(geometry[cursor], point)
        )
          cursor++;
        if (cursor < geometry.Count)
          result.Add(points[start + cursor++]);
      }
      start = end + 1;
    }
    return result;
  }
}
