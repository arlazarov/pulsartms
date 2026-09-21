using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelSearchGeometryTests
{
  [Fact]
  public void CoarseBoundsRefineToTheDetailedRoadAcrossCurvesAndRepeatedLegs()
  {
    var route = FuelGeometryFixture.RoundTrip();
    var original = new RouteGeometry(route);
    var search = new FuelSearchGeometry(route);
    var random = new Random(1409);
    Assert.InRange(
      search.BlockCount,
      1,
      FuelSearchGeometry.TargetBlockCount + route.Legs.Count
    );
    Assert.Equal(original.Miles, search.Miles, 6);
    for (var leg = 0; leg < route.Legs.Count; leg++)
    {
      var detailedLeg = new RouteGeometry(new() { Legs = [route.Legs[leg]] });
      for (var i = 0; i < 20; i++)
      {
        var mile = random.NextDouble() * 1500;
        var onRoad = detailedLeg.At(mile);
        var point = onRoad with
        {
          Latitude = onRoad.Latitude + random.NextDouble() * .2 - .1,
        };
        Equal(detailedLeg.Match(point), search.MatchLeg(leg, point));
        Equal(
          original.Match(point, leg * 1500),
          search.Match(point, leg * 1500)
        );
      }
    }
    foreach (
      var mile in new[] { -1d, 0, 150.5, 1499.5, 1500, 3000, 5999, 6001 }
    )
      Assert.True(
        RouteGeometry.Distance(original.At(mile), search.At(mile)) < 1e-7
      );
    Assert.Equal(6000, route.Miles);
    Assert.Equal(360492, route.Seconds);
    Assert.All(
      route.Legs,
      leg =>
      {
        Assert.Equal(1500, leg.Miles);
        Assert.Equal(90123, leg.Seconds);
      }
    );
    Assert.Equal(8004, route.Legs.Sum(leg => leg.Points.Count));
    Assert.Equal("Checked access warning", Assert.Single(route.Warnings));
  }

  [Fact]
  public void BoundsRemainConservativeAtHighLatitudesAndAcrossTheDateLine()
  {
    var random = new Random(83);
    var points = Enumerable
      .Range(0, 4001)
      .Select(i => new RoutePoint(
        70 + Math.Sin(i / 20d),
        i < 2000 ? 170 + i / 200d : -180 + (i - 2000) / 200d
      ))
      .ToList();
    var route = new TruckRoute
    {
      Miles = 1400,
      Legs = [new(1400, 100000, points)],
    };
    var exact = new RouteGeometry(route);
    var search = new FuelSearchGeometry(route);
    for (var i = 0; i < 60; i++)
    {
      var point = new RoutePoint(
        65 + random.NextDouble() * 10,
        -180 + random.NextDouble() * 360
      );
      Equal(exact.Match(point), search.Match(point));
    }
  }

  [Fact]
  public void ZeroLengthLegAndMandatoryBoundariesKeepTheirOrder()
  {
    var start = new RoutePoint(40, -100);
    var turn = new RoutePoint(40, -99);
    var route = new TruckRoute
    {
      Miles = 200,
      Seconds = 12000,
      Legs =
      [
        new(100, 6000, [start, turn]),
        new(0, 0, [turn, turn]),
        new(100, 6000, [turn, start]),
      ],
    };
    var exact = new RouteGeometry(route);
    var search = new FuelSearchGeometry(route);
    Equal(exact.Match(turn), search.Match(turn));
    Assert.Equal(0, search.MatchLeg(1, turn).Along);
    Assert.Equal(0, search.MatchLeg(1, turn).Away);
    Assert.Equal(turn, search.At(100));
    Assert.Equal(start, search.At(200));
    var price = new PricedFuelStation(
      new() { StationId = Guid.NewGuid(), Point = turn },
      3,
      3
    );
    var visits = FuelRouteOccurrences.Create(
      route,
      [price],
      new(),
      geometry: search
    );
    Assert.Equal(0, Assert.Single(visits).LegIndex);
  }

  [Fact]
  public void CandidateOccurrencesMatchDetailedPerLegProjectionWithoutChangingVisitIdentity()
  {
    var route = FuelGeometryFixture.RoundTrip(1501, 4);
    var prices = Enumerable
      .Range(1, 20)
      .Select(i => new PricedFuelStation(
        new()
        {
          StationId = Guid.NewGuid(),
          Point = new(40 + Math.Sin(i * Math.PI * .8) * .1, -105 + i * .9),
          Name = $"Station {i}",
        },
        3 + i * .01,
        3 + i * .01
      ))
      .ToList();
    var options = new FuelRegionOptions();
    var actual = FuelRouteOccurrences.Create(route, prices, options);
    var expected = new List<FuelCandidate>();
    for (var leg = 0; leg < route.Legs.Count; leg++)
    {
      var geometry = new RouteGeometry(new() { Legs = [route.Legs[leg]] });
      foreach (var price in prices)
      {
        var match = geometry.Match(price.Station.Point);
        Assert.InRange(match.Away, 0, options.CandidateSearchMiles);
        expected.Add(
          new(
            price.Station,
            leg * 1500 + match.Along,
            match.Away,
            0,
            price.CashUsd,
            price.EconomicUsd
          )
          {
            LegIndex = leg,
          }
        );
      }
    }
    Assert.Equal(expected.Count, actual.Count);
    foreach (var candidate in actual)
    {
      var before = Assert.Single(
        expected,
        x => x.VisitKey == candidate.VisitKey
      );
      Assert.Equal(before.AlongMiles, candidate.AlongMiles, 6);
      Assert.Equal(before.ExtraInMiles, candidate.ExtraInMiles, 6);
      Assert.Same(before.Station, candidate.Station);
    }
    var selected = FuelRouteSearch.SelectCandidates(
      actual,
      route.Miles,
      150,
      new()
      {
        Confirmed = true,
        TankGallons = 200,
        Mpg = 10,
        ReserveGallons = 25,
        FillPercent = 100,
      },
      new() { MinimumGallons = 25 },
      options
    );
    Assert.InRange(selected.Count, 1, options.CandidateShortlistLimit);
    Assert.Equal(
      selected.Count,
      selected.Select(x => x.VisitKey).Distinct().Count()
    );
  }

  [Fact]
  public void EmptyAndCancelledSearchesDoNotProduceInventedGeometry()
  {
    var empty = new FuelSearchGeometry(new());
    Assert.Equal(0, empty.BlockCount);
    Assert.Equal(0, empty.Miles);
    Assert.True(double.IsPositiveInfinity(empty.Match(new(40, -80)).Away));
    var route = FuelGeometryFixture.RoundTrip(5, 2);
    var search = new FuelSearchGeometry(route);
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Assert.Throws<OperationCanceledException>(
      () => new FuelSearchGeometry(route, cancelled.Token)
    );
    Assert.Throws<OperationCanceledException>(
      () => search.Match(new(40, -80), ct: cancelled.Token)
    );
    Assert.Throws<OperationCanceledException>(
      () => search.MatchLeg(0, new(40, -80), cancelled.Token)
    );
    Assert.Throws<OperationCanceledException>(
      () => search.At(10, cancelled.Token)
    );
  }

  private static void Equal(
    (double Along, double Away, RoutePoint Point) exact,
    (double Along, double Away, RoutePoint Point, int SegmentsExamined) actual
  )
  {
    Assert.Equal(exact.Along, actual.Along, 6);
    Assert.Equal(exact.Away, actual.Away, 8);
    Assert.True(RouteGeometry.Distance(exact.Point, actual.Point) < 1e-7);
  }
}
