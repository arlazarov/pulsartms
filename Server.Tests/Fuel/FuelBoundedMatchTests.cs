using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelBoundedMatchTests(ITestOutputHelper output)
{
  [Fact]
  public void RadiusPruningKeepsExactNearbyMatchesAcrossCurvesAndReturnLegs()
  {
    var route = FuelGeometryFixture.RoundTrip(801, 4);
    var geometry = new FuelSearchGeometry(route);
    for (var leg = 0; leg < route.Legs.Count; leg++)
    {
      foreach (var mile in new[] { 0d, 151, 731, 1499, 1500 })
      {
        var onRoad = geometry.At(leg * 1500 + mile);
        foreach (var delta in new[] { 0d, .01, .2 })
        {
          var point = onRoad with { Latitude = onRoad.Latitude + delta };
          var exact = geometry.MatchLeg(leg, point);
          var bounded = geometry.MatchLeg(leg, point, maximumAwayMiles: 40);
          Assert.InRange(exact.Away, 0, 40);
          Assert.Equal(exact.Along, bounded.Along, 8);
          Assert.Equal(exact.Away, bounded.Away, 8);
          Assert.Equal(exact.Point, bounded.Point);
          Assert.True(bounded.SegmentsExamined <= exact.SegmentsExamined);
        }
      }
    }
  }

  [Fact]
  public void DistantStationIsRejectedWithoutRefiningAnyDetailedRoadSegments()
  {
    var route = FuelGeometryFixture.RoundTrip(4001, 2);
    var geometry = new FuelSearchGeometry(route);
    var far = new RoutePoint(65, -30);
    var exact = geometry.MatchLeg(0, far);
    var bounded = geometry.MatchLeg(0, far, maximumAwayMiles: 40);
    Assert.True(exact.Away > 40);
    Assert.True(exact.SegmentsExamined > 0);
    Assert.True(double.IsPositiveInfinity(bounded.Away));
    Assert.Equal(0, bounded.SegmentsExamined);
    output.WriteLine(
      $"Distant-station fixture: exact search refined {exact.SegmentsExamined} road segments; radius-limited search refined {bounded.SegmentsExamined}. Original leg points={route.Legs[0].Points.Count}; this is work-count evidence, not production latency."
    );
  }

  [Fact]
  public void CorridorIncludesStartAndDeliveryStationsEvenWhenTheyAreNotPurchaseOccurrences()
  {
    var start = new RoutePoint(40, -100);
    var end = new RoutePoint(40, -94);
    var route = new TruckRoute
    {
      Miles = 600,
      Seconds = 36000,
      Legs = [new(600, 36000, [start, end])],
    };
    var prices = new[]
    {
      Station("Start", start),
      Station("End", end with { Latitude = 40.01 }),
      Station("Middle", new(40.01, -97)),
      Station("Distant", new(50, -97)),
    };
    var corridor = new HashSet<Guid>();
    var geometry = new FuelSearchGeometry(route);
    var candidates = FuelRouteOccurrences.Create(
      route,
      prices,
      new(),
      geometry: geometry,
      maximumAwayMiles: 40,
      corridorStations: corridor
    );
    Assert.Equal(
      prices[2].Station.StationId,
      Assert.Single(candidates).Station.StationId
    );
    var expected = prices
      .Where(price => geometry.Match(price.Station.Point).Away <= 2)
      .Select(price => price.Station.StationId)
      .ToHashSet();
    Assert.True(expected.SetEquals(corridor));
    Assert.Contains(prices[0].Station.StationId, corridor);
    Assert.Contains(prices[1].Station.StationId, corridor);
    Assert.DoesNotContain(prices[3].Station.StationId, corridor);
  }

  [Fact]
  public void BoundedOccurrencesMatchPostFilteredExactResultsAndKeepReturnVisitIdentity()
  {
    var route = new TruckRoute
    {
      Miles = 1200,
      Seconds = 72000,
      Legs =
      [
        new(600, 36000, [new(40, -100), new(40, -94)]),
        new(600, 36000, [new(40, -94), new(40, -100)]),
      ],
    };
    var prices = new[]
    {
      Station("Nearby", new(40.01, -98)),
      Station("Wide", new(40.5, -96)),
      Station("Beyond radius", new(40.7, -97)),
      Station("Far", new(55, -98)),
      Station("Turn", new(40, -94)),
    };
    var options = new FuelRegionOptions { CandidateSearchMiles = 60 };
    var geometry = new FuelSearchGeometry(route);
    var exact = FuelRouteOccurrences
      .Create(route, prices, options, geometry: geometry)
      .Where(candidate => candidate.ExtraInMiles <= 40)
      .ToList();
    var bounded = FuelRouteOccurrences.Create(
      route,
      prices,
      options,
      geometry: geometry,
      maximumAwayMiles: 40
    );
    Assert.Equal(
      exact.Select(candidate => candidate.VisitKey),
      bounded.Select(candidate => candidate.VisitKey)
    );
    Assert.Equal(
      exact.Select(candidate => candidate.AlongMiles),
      bounded.Select(candidate => candidate.AlongMiles)
    );
    Assert.Equal(
      exact.Select(candidate => candidate.ExtraInMiles),
      bounded.Select(candidate => candidate.ExtraInMiles)
    );
    Assert.Equal(
      new[] { 0, 1 },
      bounded
        .Where(candidate =>
          candidate.Station.StationId == prices[0].Station.StationId
        )
        .Select(candidate => candidate.LegIndex)
    );
    Assert.Single(
      bounded,
      candidate => candidate.Station.StationId == prices[^1].Station.StationId
    );
  }

  [Fact]
  public void RadiusSearchStillObservesCancellationBeforeFarRejection()
  {
    var geometry = new FuelSearchGeometry(FuelGeometryFixture.RoundTrip(5, 2));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Assert.Throws<OperationCanceledException>(
      () => geometry.MatchLeg(0, new(65, -30), cancelled.Token, 40)
    );
  }

  private static PricedFuelStation Station(string name, RoutePoint point) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = name,
        Point = point,
      },
      3,
      3
    );
}
