using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Bounds preserve original road measures; only nearby blocks are refined.
internal sealed partial class RouteGeometryIndex
{
  public const int TargetBlockCount = 2048;
  private const double BoundMilesPerDegree = 2 * 3958.7613 / 180;
  private readonly IReadOnlyList<RouteLeg>? borrowed;
  private readonly Coordinate[][]? captured;
  private readonly int[] pointCounts;

  private readonly record struct Coordinate(double Latitude, double Longitude);

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

  public long EstimatedBytes =>
    256L
    + blocks.LongLength * 80
    + indexes.LongLength * 24
    + pointCounts.LongLength * 4
    + (
      captured is null
        ? 0
        : captured.Sum(points => 40L + points.LongLength * 16)
    );

  public static long EstimateCaptureBytes(TruckRoute route)
  {
    var segments = route.Legs.Sum(leg =>
      (long)Math.Max(0, leg.Points.Count - 1)
    );
    var width = Math.Max(
      16L,
      (segments + TargetBlockCount - 1) / TargetBlockCount
    );
    return 256L
      + route.Legs.Sum(leg =>
        68L
        + leg.Points.Count * 16L
        + (Math.Max(0, leg.Points.Count - 1L) + width - 1) / width * 80
      );
  }

  private Coordinate Point(int leg, int index)
  {
    if (captured is not null)
      return captured[leg][index];
    var point = borrowed![leg].Points[index];
    return new(point.Latitude, point.Longitude);
  }

  public double Miles { get; }
  public int BlockCount => blocks.Length;

  public RouteGeometryIndex(
    TruckRoute route,
    bool capture,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();
    var legs = route.Legs;
    pointCounts = legs.Select(leg => leg.Points.Count).ToArray();
    if (capture)
    {
      captured = new Coordinate[legs.Count][];
      for (var leg = 0; leg < legs.Count; leg++)
      {
        ct.ThrowIfCancellationRequested();
        captured[leg] = new Coordinate[pointCounts[leg]];
        for (var i = 0; i < pointCounts[leg]; i++)
        {
          if ((i & 1023) == 0)
            ct.ThrowIfCancellationRequested();
          var p = legs[leg].Points[i];
          captured[leg][i] = new(p.Latitude, p.Longitude);
        }
      }
    }
    else
      borrowed = legs;
    var segments = legs.Sum(leg => (long)Math.Max(0, leg.Points.Count - 1));
    var width = Math.Max(
      16L,
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
}
