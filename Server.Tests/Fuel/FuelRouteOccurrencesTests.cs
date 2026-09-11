using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelRouteOccurrencesTests
{
  private static readonly RoutePoint Start = new(40, -100);
  private static readonly RoutePoint Turn = new(40, -94);
  private static readonly RoutePoint StationPoint = new(40, -98);

  private static TruckRoute Route() => new() { Miles = 1200, Seconds = 72000,
    Legs = [new(600, 36000, [Start, Turn]), new(600, 36000, [Turn, Start])] };

  private static PricedFuelStation Station(string name, RoutePoint point, double price = 3) =>
    new(new() { StationId = Guid.NewGuid(), Name = name, Point = point }, price, price);

  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 100, Mpg = 5,
    ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20 };

  [Fact]
  public void OutboundAndReturnVisitsRemainDistinctWithoutDuplicatingTheTurningBoundary()
  {
    var station = Station("Repeat", StationPoint);
    var turn = Station("Turn", Turn, 4);
    var candidates = FuelRouteOccurrences.Create(Route(), [station, station, turn], new());

    Assert.Equal(3, candidates.Count);
    var visits = candidates.Where(x => x.Station.StationId == station.Station.StationId).ToList();
    Assert.Equal(new[] { 0, 1 }, visits.Select(x => x.LegIndex));
    Assert.Equal(200, visits[0].AlongMiles, 5);
    Assert.Equal(1000, visits[1].AlongMiles, 5);
    Assert.NotEqual(visits[0].VisitKey, visits[1].VisitKey);
    Assert.Single(candidates, x => x.Station.StationId == turn.Station.StationId);
  }

  [Fact]
  public void WiderNearbyStationRetainsBothDirectedVisitsAndPaysEstimatedAccessForEach()
  {
    var start = new RoutePoint(0, 0);
    var turn = new RoutePoint(0, 6);
    var route = new TruckRoute { Miles = 1200, Seconds = 72000,
      Legs = [new(600, 36000, [start, turn]), new(600, 36000, [turn, start])] };
    static RoutePoint Away(double miles) => new(miles / 3958.7613 * 180 / Math.PI, 2);
    var station = Station("6.67 miles away", Away(6.67));
    var outside = Station("Outside 40 miles", Away(40.0001));
    var occurrences = FuelRouteOccurrences.Create(route, [station, station, outside], new(),
      maximumAwayMiles: FuelAccessEstimate.NearbyMiles);

    Assert.Equal(2, occurrences.Count);
    Assert.All(occurrences, visit =>
    {
      Assert.Equal(station.Station.StationId, visit.Station.StationId);
      Assert.Equal(6.67, visit.ExtraInMiles, 6);
    });
    Assert.Equal(new[] { 0, 1 }, occurrences.Select(x => x.LegIndex));
    Assert.Equal(200, occurrences[0].AlongMiles, 6);
    Assert.Equal(1000, occurrences[1].AlongMiles, 6);
    Assert.NotEqual(occurrences[0].VisitKey, occurrences[1].VisitKey);

    var candidates = FuelAccessEstimate.Nearby(occurrences);
    Assert.Equal(2, candidates.Count);
    Assert.All(candidates, visit =>
    {
      Assert.Equal(10.005, visit.ExtraInMiles, 6);
      Assert.Equal(10.005, visit.ExtraOutMiles, 6);
      Assert.Equal(42.02, visit.Station.DetourMinutes, 6);
    });
    Assert.Equal(occurrences.Select(x => x.VisitKey), candidates.Select(x => x.VisitKey));
  }

  [Theory]
  [InlineData(10, 1)]
  [InlineData(15, 1)]
  [InlineData(20, 1)]
  [InlineData(40, 1)]
  [InlineData(40.0001, 0)]
  public void GeographicalStationBoundaryIsFortyMilesInclusive(double away, int count)
  {
    var route = new TruckRoute { Miles = 600, Seconds = 36000,
      Legs = [new(600, 36000, [new(0, 0), new(0, 6)])] };
    var station = Station("Boundary", new(away / 3958.7613 * 180 / Math.PI, 2));
    var candidates = FuelRouteOccurrences.Create(route, [station], new(),
      maximumAwayMiles: FuelAccessEstimate.NearbyMiles);

    Assert.Equal(count, candidates.Count);
    if (count > 0) Assert.Equal(away, candidates[0].ExtraInMiles, 6);
  }

  [Fact]
  public void ChainCanBuyAtTheSameStationOnBothSidesOfTheTurn()
  {
    var station = Station("Repeat", StationPoint);
    var turn = Station("Turn", Turn, 4);
    var options = new FuelRegionOptions();
    var candidates = FuelRouteOccurrences.Create(Route(), [station, turn], options);
    var arrival = new FuelArrivalPolicy { MinimumGallons = 10, TargetGallons = 10, ReplacementPriceUsd = 4 };
    var selected = FuelRouteSearch.SelectCandidates(candidates.Concat(candidates).ToList(), 1200, 60, Profile(), arrival, options);
    var chains = FuelRouteSearch.Chains(selected, 1200, 60, Profile(), arrival);
    var result = FuelOptimizer.OptimizeWithVisits(1200, 60, Profile(), chains[0].Concat(chains[0]).ToList(), 1, false, arrivalPolicy: arrival);

    Assert.Equal(3, selected.Count);
    Assert.Equal(new[] { station.Station.StationId, turn.Station.StationId, station.Station.StationId },
      result.Plan.Stops.Select(x => x.StationId));
    Assert.Equal(new[] { candidates[0].VisitKey, candidates[1].VisitKey, candidates[2].VisitKey }, result.Purchases.Select(x => x.VisitKey));
    Assert.Equal(640, result.Plan.PurchaseCostUsd, 5);
    Assert.Equal(10, result.Plan.ArrivalGallons);
    Assert.All(result.Plan.Stops, stop => Assert.InRange(stop.ArrivalGallons, 10, 100));
    Assert.All(chains, chain => Assert.Equal(chain.Count, chain.Select(x => x.VisitKey).Distinct().Count()));
  }

  [Fact]
  public void CheckedVariantKeepsEachVisitBeforeItsOwnMandatoryStop()
  {
    var station = Station("Repeat", StationPoint);
    var candidates = FuelRouteOccurrences.Create(Route(), [station], new());
    var stops = new[] { new PlanStop(Guid.NewGuid(), "Turn", "", 1, Turn), new PlanStop(Guid.NewGuid(), "Delivery", "", 2, Start) };
    var points = FuelRouteVariant.Waypoints(Start, stops, candidates.Concat(candidates).ToList(), new(Route()), 0);
    Assert.Equal(new[] { Start, StationPoint, Turn, StationPoint, Start }, points.Select(x => x.Point));

    var raw = new TruckRoute { Miles = 1200, Seconds = 72000,
      Legs = [new(200, 12000, [Start, StationPoint]), new(400, 24000, [StationPoint, Turn]),
        new(400, 24000, [Turn, StationPoint]), new(200, 12000, [StationPoint, Start])] };
    var collapsed = FuelRouteVariant.Collapse(raw, points);
    Assert.Equal(new[] { 0, 1 }, collapsed.Stations.Select(x => x.LegIndex));
    Assert.Equal(candidates.Select(x => x.VisitKey), collapsed.Stations.Select(x => x.VisitKey));
    Assert.Equal(new[] { 600d, 600d }, collapsed.Route.Legs.Select(x => x.Miles));
  }

  [Fact]
  public void ZoneChecksKeepTheOtherPurchasesNeededOnALongRoute()
  {
    var candidates = FuelRouteOccurrences.Create(Route(), [Station("Repeat", StationPoint), Station("Turn", Turn, 4)], new());
    var seed = candidates.ToList();
    var extra = new FuelCandidate(new() { StationId = Guid.NewGuid(), Name = "Alternative" }, 650, 0, 0, 3.5, 3.5) { LegIndex = 1 };
    var scheduled = FuelZoneSearch.Schedule([seed], [extra]);
    var zone = Assert.Single(scheduled.Take(3), chain => chain.Any(x => x.VisitKey == extra.VisitKey));
    Assert.All(seed, visit => Assert.Contains(zone, x => x.VisitKey == visit.VisitKey));
    Assert.All(scheduled, chain => Assert.Equal(chain.Count, chain.Select(x => x.VisitKey).Distinct().Count()));
  }

  [Fact]
  public void CandidateSelectionRemainsBoundedAcrossRepeatedRouteLegs()
  {
    var stations = Enumerable.Range(1, 20).Select(i => Station($"Station {i}", new(40, -100 + i * .25))).ToList();
    var candidates = FuelRouteOccurrences.Create(Route(), stations, new());
    var options = new FuelRegionOptions { CandidateShortlistLimit = 8 };
    var selected = FuelRouteSearch.SelectCandidates(candidates, 1200, 100, Profile(), new(), options);
    Assert.Equal(8, selected.Count);
    Assert.Equal(selected.Count, selected.Select(x => x.VisitKey).Distinct().Count());
    Assert.InRange(FuelZoneSearch.Representatives(candidates, options).Count, 1, options.ZoneRoadChecks);
  }

  [Fact]
  public void CancelledOccurrenceSearchAndChainSearchStopImmediately()
  {
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Assert.Throws<OperationCanceledException>(() => FuelRouteOccurrences.Create(Route(), [], new(), cancellation.Token));
    Assert.Throws<OperationCanceledException>(() => FuelRouteSearch.Chains([], 1200, 100, Profile(), new(), cancellation.Token));
  }
}
