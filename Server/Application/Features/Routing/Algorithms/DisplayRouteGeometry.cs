using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class DisplayRouteGeometry
{
  public static List<RoutePoint> Simplify(IReadOnlyList<RoutePoint> points, double toleranceMeters = 2)
  {
    if (points.Count < 3) return points.ToList();
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
        if (distance > max) { max = distance; selected = i; }
      }
      if (selected < 0) continue;
      keep[selected] = true;
      pending.Push((segment.Start, selected));
      pending.Push((selected, segment.End));
    }
    return points.Where((_, i) => keep[i]).ToList();
  }
}
