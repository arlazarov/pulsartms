using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Routing;

// Telling the road search how far off the road the caller cares about must
// change what it costs and nothing else. Every test here compares the bounded
// search against the unbounded one on the same road and the same point: for a
// point inside the limit the two must agree on the projection and the
// distance, and outside it the bounded one must refuse rather than guess.
//
// The pruning is what makes this worth testing. A block is skipped on a lower
// bound of its distance, and if that bound ever over-estimates, a qualifying
// segment is thrown away and the answer is silently wrong. Agreement on a
// couple of routes would not show that, so the last test sweeps thousands of
// points across a road that doubles back on itself.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class BoundedRoadMatchTests
{
  private const double Limit = 2;

  // Taken from the road's own distance function rather than assumed, so that
  // "exactly two miles" in a test means exactly what the search means by it.
  private static readonly double MilesPerDegreeLatitude =
    RouteGeometry.Distance(new(40, -99), new(41, -99));

  private static RoutePoint NorthOf(
    double latitude,
    double longitude,
    double miles
  ) => new(latitude + miles / MilesPerDegreeLatitude, longitude);

  private static RoutePoint At(double latitude, double longitude) =>
    new(latitude, longitude);

  private static TruckRoute Road(params List<RoutePoint>[] legs) =>
    new()
    {
      Legs = legs.Select(points => new RouteLeg(
          Miles(points),
          Miles(points) * 60,
          points
        ))
        .ToList(),
      Miles = legs.Sum(Miles),
      Seconds = legs.Sum(Miles) * 60,
    };

  private static double Miles(IReadOnlyList<RoutePoint> points)
  {
    var total = 0d;
    for (var i = 1; i < points.Count; i++)
      total += RouteGeometry.Distance(points[i - 1], points[i]);
    return total;
  }

  private static List<RoutePoint> Line(
    double latitude,
    double fromLongitude,
    double toLongitude,
    int points
  ) =>
    Enumerable
      .Range(0, points)
      .Select(i =>
        At(
          latitude,
          fromLongitude
            + (toLongitude - fromLongitude) * i / (double)(points - 1)
        )
      )
      .ToList();

  [Fact]
  public void TheRoadKeepsItsLegsAndItsTotalMiles()
  {
    var road = Road(
      Line(40, -100, -99, 40),
      Line(40, -99, -98, 40),
      Line(40, -98, -97, 40)
    );
    var geometry = new FuelSearchGeometry(road);
    Assert.Equal(3, road.Legs.Count);
    // The index measures the same road the route claims.
    Assert.Equal(road.Miles, geometry.Miles, 3);
    Assert.True(geometry.Miles > 150, $"three degrees is {geometry.Miles} mi");
  }

  [Theory]
  // Comfortably inside, just inside, essentially on the line, and well outside.
  [InlineData(0.5, true)]
  [InlineData(1.9, true)]
  [InlineData(2.0001, false)]
  [InlineData(25, false)]
  public void InsideTheLimitBothSearchesAgree(double awayMiles, bool inside)
  {
    var road = Road(Line(40, -100, -98, 200), Line(40, -98, -96, 200));
    var geometry = new FuelSearchGeometry(road);
    var point = NorthOf(40, -99, awayMiles);

    var open = geometry.Match(point);
    var bounded = geometry.Match(point, maximumAwayMiles: Limit);

    Assert.Equal(inside, open.Away <= Limit);
    if (inside)
    {
      Assert.Equal(open.Along, bounded.Along, 6);
      Assert.Equal(open.Away, bounded.Away, 6);
      Assert.Equal(open.Point.Latitude, bounded.Point.Latitude, 9);
      Assert.Equal(open.Point.Longitude, bounded.Point.Longitude, 9);
    }
    else
    {
      // A refusal, not a wrong answer at mile zero.
      Assert.True(double.IsPositiveInfinity(bounded.Away));
      Assert.False(bounded.Away <= Limit);
    }
  }

  [Fact]
  public void TheBoundaryIsDecidedTheSameWayByBothSearches()
  {
    // Placing a point at "exactly two miles" is not something floating point
    // will do: the first attempt landed at 2.0000000000000968, which the
    // filter's own `Away <= 2` rejects as well. So the boundary is tested as
    // the invariant it actually is - whatever the open search decides about a
    // point, the bounded search must decide the same - swept finely enough
    // that points land on both sides of the limit and some land on it.
    var road = Road(Line(40, -100, -98, 400));
    var geometry = new FuelSearchGeometry(road);
    var accepted = 0;
    var rejected = 0;
    for (var offset = -60; offset <= 60; offset++)
    {
      var candidate = NorthOf(40, -99, Limit + offset * 1e-9);
      var open = geometry.Match(candidate);
      var bounded = geometry.Match(candidate, maximumAwayMiles: Limit);
      if (open.Away <= Limit)
      {
        accepted++;
        Assert.False(
          double.IsPositiveInfinity(bounded.Away),
          $"{open.Away:R} mi is within {Limit} and was discarded"
        );
        Assert.Equal(open.Away, bounded.Away, 9);
        Assert.Equal(open.Along, bounded.Along, 9);
      }
      else
      {
        rejected++;
        Assert.False(bounded.Away <= Limit);
      }
    }
    // The sweep has to have straddled the limit, or it proved nothing.
    Assert.True(accepted > 0, "nothing landed inside the limit");
    Assert.True(rejected > 0, "nothing landed outside the limit");
  }

  [Fact]
  public void AStartingMileStillMovesTheAnswerForward()
  {
    // A road that passes the same place twice: out along 40N and back along a
    // hair north of it. A point between them matches whichever the caller
    // allows, and the limit must not change which.
    var road = Road(Line(40, -100, -98, 300), Line(40.02, -98, -100, 300));
    var geometry = new FuelSearchGeometry(road);
    var between = At(40.01, -99);

    var first = geometry.Match(between);
    var firstBounded = geometry.Match(between, maximumAwayMiles: Limit);
    Assert.Equal(first.Along, firstBounded.Along, 6);
    Assert.Equal(first.Away, firstBounded.Away, 6);

    var after = first.Along + 10;
    var second = geometry.Match(between, after);
    var secondBounded = geometry.Match(between, after, maximumAwayMiles: Limit);
    Assert.True(
      second.Along >= after - 1e-6,
      $"a search from mile {after} returned {second.Along}"
    );
    Assert.NotEqual(first.Along, second.Along, 3);
    Assert.Equal(second.Along, secondBounded.Along, 6);
    Assert.Equal(second.Away, secondBounded.Away, 6);
  }

  [Fact]
  public void TheLowerBoundNeverDiscardsAQualifyingSegment()
  {
    // A road that doubles back and crosses itself, so a point can be near
    // several distant parts of it at once and the pruning has to be right
    // about all of them.
    var road = Road(
      Line(40, -100, -98, 260),
      Line(40.5, -98, -100, 260),
      Enumerable
        .Range(0, 260)
        .Select(i => At(40 + 0.5 * i / 259.0, -100 + 2.0 * i / 259.0))
        .ToList()
    );
    var geometry = new FuelSearchGeometry(road);

    // An unsafe bound only misjudges points close to the limit: further in,
    // even an inflated distance stays under it, and further out both forms
    // reject anyway. So the sweep walks the road and steps off it at offsets
    // that crowd the limit from below on both sides, and adds a coarse grid
    // and the road's own vertices for everything else.
    double[] offsets =
    [
      0,
      0.1,
      0.5,
      1.0,
      1.5,
      1.8,
      1.9,
      1.95,
      1.98,
      1.99,
      1.995,
      1.999,
      2.001,
      2.05,
      2.5,
      5,
    ];
    var candidates = new List<RoutePoint>();
    foreach (var leg in road.Legs)
      for (var i = 0; i < leg.Points.Count; i += 7)
        foreach (var offset in offsets)
        {
          candidates.Add(
            NorthOf(leg.Points[i].Latitude, leg.Points[i].Longitude, offset)
          );
          candidates.Add(
            NorthOf(leg.Points[i].Latitude, leg.Points[i].Longitude, -offset)
          );
        }
    for (var lat = 39.8; lat <= 40.7; lat += 0.02)
    for (var lon = -100.2; lon <= -97.8; lon += 0.02)
      candidates.Add(At(Math.Round(lat, 4), Math.Round(lon, 4)));
    foreach (var leg in road.Legs)
    foreach (var point in leg.Points)
      candidates.Add(point);

    var agreed = 0;
    var qualified = 0;
    foreach (var candidate in candidates)
    {
      var open = geometry.Match(candidate);
      var bounded = geometry.Match(candidate, maximumAwayMiles: Limit);
      if (open.Away <= Limit)
      {
        qualified++;
        // The bound may not reject what the open search accepted, and must
        // return the same projection when it accepts.
        Assert.False(
          double.IsPositiveInfinity(bounded.Away),
          $"({candidate.Latitude}, {candidate.Longitude}) is "
            + $"{open.Away:F4} mi away and was discarded"
        );
        Assert.Equal(open.Away, bounded.Away, 6);
        Assert.Equal(open.Along, bounded.Along, 6);
        Assert.Equal(open.Point.Latitude, bounded.Point.Latitude, 9);
        Assert.Equal(open.Point.Longitude, bounded.Point.Longitude, 9);
        agreed++;
      }
      else
      {
        Assert.False(bounded.Away <= Limit);
      }
    }

    // A sweep that qualified nothing would pass while testing nothing.
    Assert.True(candidates.Count > 5000, $"only {candidates.Count} points");
    Assert.True(qualified > 200, $"only {qualified} points were in range");
    Assert.Equal(qualified, agreed);
  }

  [Fact]
  public void TheBoundedSearchExaminesFarLessOfTheRoad()
  {
    var road = Road(Line(40, -100, -94, 6000));
    var geometry = new FuelSearchGeometry(road);
    // Far enough that the unbounded search has no near distance to prune with.
    var distant = At(44, -97);

    var open = geometry.Match(distant);
    var bounded = geometry.Match(distant, maximumAwayMiles: Limit);

    Assert.True(open.Away > Limit);
    Assert.True(double.IsPositiveInfinity(bounded.Away));
    Assert.True(
      open.SegmentsExamined > 1000,
      $"the open search examined only {open.SegmentsExamined}"
    );
    Assert.True(
      bounded.SegmentsExamined < open.SegmentsExamined / 10,
      $"bounded examined {bounded.SegmentsExamined} against "
        + $"{open.SegmentsExamined}"
    );
  }
}
