using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

// Request-scoped coarse bounds reference the checked points; refinement keeps
// exact road-mile positions.
public sealed class FuelSearchGeometry
{
  public const int TargetBlockCount = 2048;
  private const double BoundMilesPerDegree = 2 * 3958.7613 / 180;
  private readonly IReadOnlyList<RouteLeg> legs;
  private readonly Block[] blocks;
  private readonly LegIndex[] indexes;

  private readonly record struct Block(
    int Leg,
    int First,
    int Last,
    double StartMiles,
    double EndMiles,
    double South,
    double North,
    double West,
    double East,
    double MinimumCosine
  );

  private readonly record struct LegIndex(
    int FirstBlock,
    int BlockCount,
    double StartMiles,
    double Scale
  );

  public double Miles { get; }
  public int BlockCount => blocks.Length;

  public FuelSearchGeometry(TruckRoute route, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();
    legs = route.Legs;
    var segments = legs.Sum(leg => (long)Math.Max(0, leg.Points.Count - 1));
    var width = Math.Max(
      1L,
      (segments + TargetBlockCount - 1) / TargetBlockCount
    );
    blocks = new Block[
      legs.Sum(leg =>
        (int)((Math.Max(0, leg.Points.Count - 1) + width - 1) / width)
      )
    ];
    indexes = new LegIndex[legs.Count];
    double offset = 0;
    var blockIndex = 0;
    for (var legIndex = 0; legIndex < legs.Count; legIndex++)
    {
      ct.ThrowIfCancellationRequested();
      var leg = legs[legIndex];
      double length = 0;
      for (var i = 1; i < leg.Points.Count; i++)
      {
        if ((i & 1023) == 0)
          ct.ThrowIfCancellationRequested();
        length += RouteGeometry.Distance(leg.Points[i - 1], leg.Points[i]);
      }
      var scale = length > 0 ? leg.Miles / length : 0;
      var firstBlock = blockIndex;
      var startMiles = offset;
      for (var first = 0; first < leg.Points.Count - 1; )
      {
        ct.ThrowIfCancellationRequested();
        var last = (int)Math.Min(leg.Points.Count - 1L, first + width);
        var south = leg.Points[first].Latitude;
        var north = south;
        var west = leg.Points[first].Longitude;
        var east = west;
        var blockStart = offset;
        for (var i = first + 1; i <= last; i++)
        {
          if ((i & 1023) == 0)
            ct.ThrowIfCancellationRequested();
          var point = leg.Points[i];
          south = Math.Min(south, point.Latitude);
          north = Math.Max(north, point.Latitude);
          west = Math.Min(west, point.Longitude);
          east = Math.Max(east, point.Longitude);
          offset += RouteGeometry.Distance(leg.Points[i - 1], point) * scale;
        }
        blocks[blockIndex++] = new(
          legIndex,
          first,
          last,
          blockStart,
          offset,
          south,
          north,
          west,
          east,
          Math.Cos(Math.Max(Math.Abs(south), Math.Abs(north)) * Math.PI / 180)
        );
        first = last;
      }
      indexes[legIndex] = new(
        firstBlock,
        blockIndex - firstBlock,
        startMiles,
        scale
      );
    }
    Miles = offset;
  }

  public (
    double Along,
    double Away,
    RoutePoint Point,
    int SegmentsExamined
  ) Match(
    RoutePoint point,
    double minAlong = 0,
    CancellationToken ct = default
  ) => Match(point, minAlong, 0, blocks.Length, ct);

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
    double maximumAwayMiles = double.PositiveInfinity
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
      var points = legs[block.Leg].Points;
      var scale = indexes[block.Leg].Scale;
      var along = block.StartMiles;
      for (var i = block.First; i < block.Last; i++)
      {
        if ((i & 1023) == 0)
          ct.ThrowIfCancellationRequested();
        var a = points[i];
        var end = points[i + 1];
        var miles = RouteGeometry.Distance(a, end) * scale;
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
    var points = legs[block.Leg].Points;
    var along = block.StartMiles;
    for (var i = block.First; i < block.Last; i++)
    {
      if ((i & 1023) == 0)
        ct.ThrowIfCancellationRequested();
      var a = points[i];
      var end = points[i + 1];
      var miles = RouteGeometry.Distance(a, end) * indexes[block.Leg].Scale;
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
    return points[block.Last];
  }
}
