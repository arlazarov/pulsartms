using System.Text.Json;

namespace RoutePointShapeProbe;

// Two model sets over identical JSON bytes. The only difference between them
// is whether a route point is a heap object or a value inside the array. Every
// other property, name and serializer option is the same, so any measured
// difference belongs to that one decision.

// --- the shape the codebase has today ---------------------------------------
public sealed record RoutePointClass(double Latitude, double Longitude);

public sealed record RouteLegClass(
  double Miles,
  double Seconds,
  List<RoutePointClass> Points
);

public sealed class TruckRouteClass
{
  public DateTime CalculatedAt { get; set; }
  public double Miles { get; set; }
  public double Seconds { get; set; }
  public List<RouteLegClass> Legs { get; set; } = [];
  public List<string> Warnings { get; set; } = [];
}

// --- the shape under consideration -------------------------------------------
public readonly record struct RoutePointValue(
  double Latitude,
  double Longitude
);

public sealed record RouteLegValue(
  double Miles,
  double Seconds,
  List<RoutePointValue> Points
);

public sealed class TruckRouteValue
{
  public DateTime CalculatedAt { get; set; }
  public double Miles { get; set; }
  public double Seconds { get; set; }
  public List<RouteLegValue> Legs { get; set; } = [];
  public List<string> Warnings { get; set; } = [];
}

public static class ProbeJson
{
  public static readonly JsonSerializerOptions Options = new(
    JsonSerializerDefaults.Web
  );
}

// Douglas-Peucker at the same two-metre tolerance TrimForDisplay applies before
// it serialises displayJson. It runs once, to produce a display payload with a
// realistic point count. It is never itself measured, and both model sets are
// then handed the same bytes.
public static class DisplaySimplification
{
  private const double MetersPerDegree = 111_320d;

  public static List<RoutePointClass> Run(
    IReadOnlyList<RoutePointClass> points,
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
      var (start, end) = segment;
      if (end <= start + 1)
        continue;
      var worst = -1d;
      var index = -1;
      for (var i = start + 1; i < end; i++)
      {
        var distance = Perpendicular(points[i], points[start], points[end]);
        if (distance > worst)
        {
          worst = distance;
          index = i;
        }
      }
      if (worst <= toleranceMeters || index < 0)
        continue;
      keep[index] = true;
      pending.Push((start, index));
      pending.Push((index, end));
    }
    var result = new List<RoutePointClass>();
    for (var i = 0; i < points.Count; i++)
      if (keep[i])
        result.Add(points[i]);
    return result;
  }

  private static double Perpendicular(
    RoutePointClass point,
    RoutePointClass a,
    RoutePointClass b
  )
  {
    var cosine = Math.Cos(a.Latitude * Math.PI / 180);
    var px = (point.Longitude - a.Longitude) * cosine * MetersPerDegree;
    var py = (point.Latitude - a.Latitude) * MetersPerDegree;
    var bx = (b.Longitude - a.Longitude) * cosine * MetersPerDegree;
    var by = (b.Latitude - a.Latitude) * MetersPerDegree;
    var length = bx * bx + by * by;
    if (length <= 0)
      return Math.Sqrt(px * px + py * py);
    var t = Math.Clamp((px * bx + py * by) / length, 0, 1);
    var dx = px - t * bx;
    var dy = py - t * by;
    return Math.Sqrt(dx * dx + dy * dy);
  }
}

public readonly record struct Sample(
  double Bytes,
  double Milliseconds,
  double Gen0,
  double Gen1,
  double Gen2
);

public sealed class Samples
{
  private readonly List<Sample> taken = [];

  public void Add(Sample sample) => taken.Add(sample);

  public Sample Median()
  {
    double Mid(Func<Sample, double> read)
    {
      var sorted = taken.Select(read).OrderBy(x => x).ToArray();
      return sorted[sorted.Length / 2];
    }
    return new(
      Mid(x => x.Bytes),
      Mid(x => x.Milliseconds),
      Mid(x => x.Gen0),
      Mid(x => x.Gen1),
      Mid(x => x.Gen2)
    );
  }
}
