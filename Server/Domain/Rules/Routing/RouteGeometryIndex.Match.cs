using Domain.Models.Routing;

namespace Domain.Rules.Routing;

internal sealed partial class RouteGeometryIndex
{
  public (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) Match(
    RoutePoint point,
    double minAlong = 0,
    CancellationToken ct = default,
    bool strictMinimum = false
  ) =>
    Match(point, minAlong, 0, blocks.Length, ct, strictMinimum: strictMinimum);

  public (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) MatchLeg(
    int leg,
    RoutePoint point,
    CancellationToken ct = default,
    double maximumAwayMiles = double.PositiveInfinity
  )
  {
    var index = indexes[leg];
    if (index.BlockCount == 0)
    {
      ct.ThrowIfCancellationRequested();
      return (0, double.PositiveInfinity, point, 0);
    }
    var match = Match(
      point,
      index.StartMiles,
      index.FirstBlock,
      index.BlockCount,
      ct,
      maximumAwayMiles
    );
    return (
      match.Along - index.StartMiles,
      match.Away,
      match.Point,
      match.SegmentsExamined
    );
  }

  private (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) Match(
    RoutePoint point,
    double minAlong,
    int firstBlock,
    int count,
    CancellationToken ct,
    double maximumAwayMiles = double.PositiveInfinity,
    bool strictMinimum = false
  )
  {
    ct.ThrowIfCancellationRequested();
    var bestAlong = 0d;
    var bestAway = double.PositiveInfinity;
    var latitude = point.Latitude;
    var longitude = point.Longitude;
    var examined = 0;
    var cosine = Math.Cos(point.Latitude * Math.PI / 180);
    var closest = -1;
    var closestBound = double.PositiveInfinity;
    for (var b = firstBlock; b < firstBlock + count; b++)
    {
      ct.ThrowIfCancellationRequested();
      if (blocks[b].EndMiles < minAlong)
        continue;
      var bound = LowerBoundSquared(blocks[b], point, cosine);
      if (bound < closestBound)
      {
        closest = b;
        closestBound = bound;
      }
    }
    if (closestBound > maximumAwayMiles * maximumAwayMiles)
      return (0, double.PositiveInfinity, point, 0);
    for (var pass = -1; pass < count; pass++)
    {
      ct.ThrowIfCancellationRequested();
      var b = pass < 0 ? closest : firstBlock + pass;
      if (b < 0 || pass >= 0 && b == closest)
        continue;
      var block = blocks[b];
      var searchDistance = Math.Min(bestAway, maximumAwayMiles);
      if (
        block.EndMiles < minAlong
        || LowerBoundSquared(block, point, cosine)
          > searchDistance * searchDistance
      )
        continue;
      var leg = block.Leg;
      var scale = indexes[block.Leg].Scale;
      var along = block.StartMiles;
      for (var i = block.First; i < block.Last; i++)
      {
        if ((i & 1023) == 0)
          ct.ThrowIfCancellationRequested();
        var a = Point(leg, i);
        var end = Point(leg, i + 1);
        var miles =
          RouteGeometry.Distance(
            a.Latitude,
            a.Longitude,
            end.Latitude,
            end.Longitude
          ) * scale;
        examined++;
        if (along + miles >= minAlong)
        {
          var x = (end.Longitude - a.Longitude) * cosine;
          var y = end.Latitude - a.Latitude;
          var denominator = x * x + y * y;
          var t =
            denominator == 0
              ? 0
              : Math.Clamp(
                (
                  (point.Longitude - a.Longitude) * cosine * x
                  + (point.Latitude - a.Latitude) * y
                ) / denominator,
                0,
                1
              );
          if (strictMinimum && miles > 0)
            t = Math.Max(t, Math.Clamp((minAlong - along) / miles, 0, 1));
          var projectedLatitude = a.Latitude + y * t;
          var projectedLongitude =
            a.Longitude + (end.Longitude - a.Longitude) * t;
          var away = RouteGeometry.Distance(
            point.Latitude,
            point.Longitude,
            projectedLatitude,
            projectedLongitude
          );
          var candidateAlong = along + t * miles;
          if (
            away <= maximumAwayMiles
            && (
              away < bestAway || away == bestAway && candidateAlong < bestAlong
            )
          )
          {
            bestAlong = candidateAlong;
            bestAway = away;
            latitude = projectedLatitude;
            longitude = projectedLongitude;
          }
        }
        along += miles;
      }
    }
    return (bestAlong, bestAway, new(latitude, longitude), examined);
  }

  // sin(x) >= 2x/pi gives a conservative spherical bound, including across the
  // date line.
  private static double LowerBoundSquared(
    Block block,
    RoutePoint point,
    double cosine
  )
  {
    var latitude = Math.Max(
      0,
      Math.Max(block.South - point.Latitude, point.Latitude - block.North)
    );
    var longitude = 0d;
    if (point.Longitude < block.West || point.Longitude > block.East)
    {
      var west = Math.Abs(point.Longitude - block.West);
      var east = Math.Abs(point.Longitude - block.East);
      longitude = Math.Min(
        Math.Min(west, 360 - west),
        Math.Min(east, 360 - east)
      );
    }
    return BoundMilesPerDegree
      * BoundMilesPerDegree
      * (
        latitude * latitude
        + cosine * block.MinimumCosine * longitude * longitude
      );
  }

  public RoutePoint At(double mile, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();
    if (blocks.Length == 0)
      return new(0, 0);
    var lo = 0;
    var hi = blocks.Length - 1;
    while (lo < hi)
    {
      var mid = lo + (hi - lo) / 2;
      if (blocks[mid].EndMiles < mile)
        lo = mid + 1;
      else
        hi = mid;
    }
    var block = blocks[lo];
    var leg = block.Leg;
    var along = block.StartMiles;
    for (var i = block.First; i < block.Last; i++)
    {
      if ((i & 1023) == 0)
        ct.ThrowIfCancellationRequested();
      var a = Point(leg, i);
      var end = Point(leg, i + 1);
      var miles =
        RouteGeometry.Distance(
          a.Latitude,
          a.Longitude,
          end.Latitude,
          end.Longitude
        ) * indexes[block.Leg].Scale;
      if (along + miles >= mile)
      {
        var t = miles > 0 ? Math.Clamp((mile - along) / miles, 0, 1) : 0;
        return new(
          a.Latitude + (end.Latitude - a.Latitude) * t,
          a.Longitude + (end.Longitude - a.Longitude) * t
        );
      }
      along += miles;
    }
    var last = Point(leg, block.Last);
    return new(last.Latitude, last.Longitude);
  }
}
