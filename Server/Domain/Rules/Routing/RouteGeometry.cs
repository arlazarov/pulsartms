using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public sealed class RouteGeometry
{
  private readonly List<(
    double ALat,
    double ALon,
    double BLat,
    double BLon,
    double Start,
    double Miles
  )> segments;
  public double Miles { get; }

  public RouteGeometry(TruckRoute route)
  {
    segments = new(route.Legs.Sum(x => Math.Max(0, x.Points.Count - 1)));
    double offset = 0;
    foreach (var leg in route.Legs)
    {
      var start = segments.Count;
      double sum = 0;
      for (var i = 1; i < leg.Points.Count; i++)
      {
        var a = leg.Points[i - 1];
        var b = leg.Points[i];
        var length = Distance(a, b);
        sum += length;
        segments.Add(
          (a.Latitude, a.Longitude, b.Latitude, b.Longitude, 0, length)
        );
      }
      for (var i = start; i < segments.Count; i++)
      {
        var segment = segments[i];
        var length = sum > 0 ? segment.Miles / sum * leg.Miles : 0;
        segments[i] = (
          segment.ALat,
          segment.ALon,
          segment.BLat,
          segment.BLon,
          offset,
          length
        );
        offset += length;
      }
    }
    Miles = offset;
  }

  public (double Along, double Away, RoutePoint Point) Match(
    RoutePoint p,
    double minAlong = 0
  )
  {
    var best = (
      Along: 0d,
      Away: double.PositiveInfinity,
      Latitude: p.Latitude,
      Longitude: p.Longitude
    );
    var scale = Math.Cos(p.Latitude * Math.PI / 180);
    foreach (var s in segments)
    {
      if (s.Start + s.Miles < minAlong)
        continue;
      var x = (s.BLon - s.ALon) * scale;
      var y = s.BLat - s.ALat;
      var denominator = x * x + y * y;
      var t =
        denominator == 0
          ? 0
          : Math.Clamp(
            ((p.Longitude - s.ALon) * scale * x + (p.Latitude - s.ALat) * y)
              / denominator,
            0,
            1
          );
      var latitude = s.ALat + (s.BLat - s.ALat) * t;
      var longitude = s.ALon + (s.BLon - s.ALon) * t;
      var away = Distance(p.Latitude, p.Longitude, latitude, longitude);
      if (away < best.Away)
        best = (s.Start + t * s.Miles, away, latitude, longitude);
    }
    return (best.Along, best.Away, new(best.Latitude, best.Longitude));
  }

  public RoutePoint At(double mile)
  {
    foreach (var s in segments)
    {
      if (s.Start + s.Miles < mile)
        continue;
      var t = s.Miles > 0 ? Math.Clamp((mile - s.Start) / s.Miles, 0, 1) : 0;
      return new(
        s.ALat + (s.BLat - s.ALat) * t,
        s.ALon + (s.BLon - s.ALon) * t
      );
    }
    return segments.Count > 0
      ? new(segments[^1].BLat, segments[^1].BLon)
      : new(0, 0);
  }

  public static double Distance(RoutePoint a, RoutePoint b) =>
    Distance(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

  public static double Distance(
    double aLatitude,
    double aLongitude,
    double bLatitude,
    double bLongitude
  )
  {
    var lat = (bLatitude - aLatitude) * Math.PI / 180;
    var lng = (bLongitude - aLongitude) * Math.PI / 180;
    var h =
      Math.Pow(Math.Sin(lat / 2), 2)
      + Math.Cos(aLatitude * Math.PI / 180)
        * Math.Cos(bLatitude * Math.PI / 180)
        * Math.Pow(Math.Sin(lng / 2), 2);
    return 3958.7613 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
  }
}
