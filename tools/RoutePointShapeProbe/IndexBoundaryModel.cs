namespace RoutePointShapeProbe;

// A MODEL of the index boundary. This is not RouteGeometryIndex and it does not
// measure RouteGeometryIndex.Match or .At.
//
// What it reproduces faithfully: the real index stores coordinates as value
// types inside (`readonly record struct Coordinate`) and hands a RoutePoint
// back across its own API. That boundary is the only thing under test, and the
// two classes below are identical inside so that nothing else can move.
//
// What it does NOT reproduce: the real Match narrows by block with a cosine
// bound; the Match here is a linear scan. So the TIME reported for this
// scenario is a property of this file and means nothing about production. Read
// only the allocated bytes, which are exactly the boundary's own cost and are
// verifiable by hand: one returned point per call, 32 bytes each on 64-bit.
public readonly record struct Coordinate(double Latitude, double Longitude);

public abstract class BoundaryModel
{
  protected readonly Coordinate[] Points;
  protected readonly double[] Cumulative;

  public double Miles => Cumulative[^1];

  protected BoundaryModel(IReadOnlyList<RoutePointClass> source)
  {
    Points = new Coordinate[source.Count];
    Cumulative = new double[source.Count];
    for (var i = 0; i < source.Count; i++)
      Points[i] = new(source[i].Latitude, source[i].Longitude);
    for (var i = 1; i < Points.Length; i++)
      Cumulative[i] = Cumulative[i - 1] + Distance(Points[i - 1], Points[i]);
  }

  protected static double Distance(Coordinate a, Coordinate b)
  {
    var cosine = Math.Cos(a.Latitude * Math.PI / 180);
    var x = (b.Longitude - a.Longitude) * cosine * 69.0;
    var y = (b.Latitude - a.Latitude) * 69.0;
    return Math.Sqrt(x * x + y * y);
  }

  protected (int Index, double Fraction) Locate(double mile)
  {
    var lo = 0;
    var hi = Cumulative.Length - 1;
    while (lo < hi)
    {
      var mid = lo + (hi - lo) / 2;
      if (Cumulative[mid] < mile)
        lo = mid + 1;
      else
        hi = mid;
    }
    if (lo == 0)
      return (0, 0);
    var span = Cumulative[lo] - Cumulative[lo - 1];
    return (
      lo - 1,
      span > 0 ? Math.Clamp((mile - Cumulative[lo - 1]) / span, 0, 1) : 0
    );
  }
}

// Values inside, an object on the way out - as the code stands.
public sealed class BoundaryReturningClass(
  IReadOnlyList<RoutePointClass> source
) : BoundaryModel(source)
{
  public RoutePointClass At(double mile)
  {
    var (i, fraction) = Locate(mile);
    var a = Points[i];
    var b = Points[Math.Min(i + 1, Points.Length - 1)];
    return new(
      a.Latitude + (b.Latitude - a.Latitude) * fraction,
      a.Longitude + (b.Longitude - a.Longitude) * fraction
    );
  }

  public (double Along, double Away, RoutePointClass Point) Match(
    RoutePointClass to
  )
  {
    var best = double.MaxValue;
    var at = 0;
    for (var i = 0; i < Points.Length; i++)
    {
      var distance = Distance(Points[i], new(to.Latitude, to.Longitude));
      if (distance < best)
      {
        best = distance;
        at = i;
      }
    }
    var point = Points[at];
    return (Cumulative[at], best, new(point.Latitude, point.Longitude));
  }
}

// A value all the way out.
public sealed class BoundaryReturningValue(
  IReadOnlyList<RoutePointClass> source
) : BoundaryModel(source)
{
  public RoutePointValue At(double mile)
  {
    var (i, fraction) = Locate(mile);
    var a = Points[i];
    var b = Points[Math.Min(i + 1, Points.Length - 1)];
    return new(
      a.Latitude + (b.Latitude - a.Latitude) * fraction,
      a.Longitude + (b.Longitude - a.Longitude) * fraction
    );
  }

  public (double Along, double Away, RoutePointValue Point) Match(
    RoutePointValue to
  )
  {
    var best = double.MaxValue;
    var at = 0;
    for (var i = 0; i < Points.Length; i++)
    {
      var distance = Distance(Points[i], new(to.Latitude, to.Longitude));
      if (distance < best)
      {
        best = distance;
        at = i;
      }
    }
    var point = Points[at];
    return (Cumulative[at], best, new(point.Latitude, point.Longitude));
  }
}
