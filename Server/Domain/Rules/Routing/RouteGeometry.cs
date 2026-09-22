using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public sealed class RouteGeometry
{
  private readonly RouteGeometryIndex index;
  public double Miles => index.Miles;
  public long EstimatedBytes => index.EstimatedBytes;

  public static long EstimateBytes(TruckRoute route) =>
    RouteGeometryIndex.EstimateCaptureBytes(route);

  public RouteGeometry(TruckRoute route)
  {
    index = new(route, capture: true);
  }

  public (double Along, double Away, RoutePoint Point) Match(
    RoutePoint point,
    double minAlong = 0
  )
  {
    var result = index.Match(point, minAlong);
    return (result.Along, result.Away, result.Point);
  }

  public (double Along, double Away, RoutePoint Point) MatchAfter(
    RoutePoint point,
    double minAlong
  )
  {
    var result = index.Match(point, minAlong, strictMinimum: true);
    return (result.Along, result.Away, result.Point);
  }

  public RoutePoint At(double mile) => index.At(mile);

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
