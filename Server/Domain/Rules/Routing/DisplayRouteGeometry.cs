using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class DisplayRouteGeometry
{
  public static List<RoutePoint> Simplify(
    IReadOnlyList<RoutePoint> points,
    double toleranceMeters = 2
  )
  {
    if (points.Count < 3)
      return points.ToList();
    var keep = new bool[points.Count];
    keep[0] = keep[^1] = true;
    var pending = new Stack<(int Start, int End)>();
    pending.Push((0, points.Count - 1));
    while (pending.TryPop(out var segment))
    {
      var a = points[segment.Start];
      var b = points[segment.End];
      var scale = Math.Cos((a.Latitude + b.Latitude) * Math.PI / 360);
      var dx = (b.Longitude - a.Longitude) * scale * 111320;
      var dy = (b.Latitude - a.Latitude) * 111320;
      var length = dx * dx + dy * dy;
      var max = toleranceMeters * toleranceMeters;
      var selected = -1;
      for (var i = segment.Start + 1; i < segment.End; i++)
      {
        var x = (points[i].Longitude - a.Longitude) * scale * 111320;
        var y = (points[i].Latitude - a.Latitude) * 111320;
        var t = length > 0 ? Math.Clamp((x * dx + y * dy) / length, 0, 1) : 0;
        var distance = Math.Pow(x - t * dx, 2) + Math.Pow(y - t * dy, 2);
        if (distance > max)
        {
          max = distance;
          selected = i;
        }
      }
      if (selected < 0)
        continue;
      keep[selected] = true;
      pending.Push((segment.Start, selected));
      pending.Push((selected, segment.End));
    }
    return points.Where((_, i) => keep[i]).ToList();
  }

  // The reference road exists on the map for one purpose: to draw what lies
  // behind the truck. The map finds the truck's origin on it and throws the
  // rest away, so everything past that point was sent, parsed and measured
  // for nothing - more than half of a long route's payload.
  //
  // The search is the map's own, deliberately: nearest segment on a plane
  // scaled at the origin's latitude, the first of equals, nothing beyond two
  // miles. The head keeps the matched segment whole, so the map's search over
  // it lands on the same segment it would have found in the full road. No
  // match means the map draws the whole reference separately, and it is left
  // alone.
  public static List<RouteLeg> TravelledHead(
    IReadOnlyList<RouteLeg> reference,
    RoutePoint origin
  )
  {
    const double limit = 2d / 69 * (2d / 69);
    var scale = Math.Cos(origin.Latitude * Math.PI / 180);
    var best = double.PositiveInfinity;
    var (bestLeg, bestPoint) = (-1, -1);
    for (var leg = 0; leg < reference.Count; leg++)
    {
      var points = reference[leg].Points;
      for (var i = 1; i < points.Count; i++)
      {
        var dx = (points[i].Longitude - points[i - 1].Longitude) * scale;
        var dy = points[i].Latitude - points[i - 1].Latitude;
        var x = (origin.Longitude - points[i - 1].Longitude) * scale;
        var y = origin.Latitude - points[i - 1].Latitude;
        var length = dx * dx + dy * dy;
        var t = length > 0 ? Math.Clamp((x * dx + y * dy) / length, 0, 1) : 0;
        var distance = Math.Pow(x - t * dx, 2) + Math.Pow(y - t * dy, 2);
        if (distance < best)
          (best, bestLeg, bestPoint) = (distance, leg, i);
      }
    }
    if (bestLeg < 0 || best >= limit)
      return reference.ToList();
    return
    [
      .. reference.Take(bestLeg),
      reference[bestLeg] with
      {
        Points = reference[bestLeg].Points.Take(bestPoint + 1).ToList(),
      },
    ];
  }
}
