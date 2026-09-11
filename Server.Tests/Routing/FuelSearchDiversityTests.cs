using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Server.Tests.Routing;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelSearchDiversityTests
{
  private static FuelCandidate Station(string name, double mile, double price, double access, int leg = 0) =>
    new(new() { StationId = Guid.NewGuid(), Name = name, Point = new(40, -80), YourPrice = price },
      mile, access, 0, price, price) { LegIndex = leg };

  [Fact]
  public void SpareShortlistSlotsIncludeBalancedPriceAndAccessInsteadOfOnlyCheapestDetours()
  {
    var near = Station("Near but expensive", 40, 6, 0);
    var cheapest = Station("Cheapest distant", 42, 5, 30);
    var dominated = Station("Slightly dearer distant", 44, 5.05, 35);
    var balanced = Station("Moderate price and access", 46, 5.1, 5);
    var later = Station("Later section", 300, 6.5, 0);
    var candidates = new[] { near, cheapest, dominated, balanced, later };
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, FillPercent = 100,
      ReserveGallons = 10, Mpg = 5 };

    var selected = FuelRouteSearch.SelectCandidates(candidates, 400, 30, profile,
      new() { MinimumGallons = 10, TargetGallons = 10, ReplacementPriceUsd = 6.5 },
      new() { CandidateShortlistLimit = 8 });

    Assert.Contains(balanced, selected);
    Assert.Contains(near, selected);
    Assert.Contains(later, selected);
    Assert.InRange(selected.Count, 1, 8);
    Assert.Equal(selected.Count, selected.DistinctBy(x => x.VisitKey).Count());
  }

  [Fact]
  public void PriceAccessDominanceDoesNotCrossVisitLegs()
  {
    var near = Station("Near", 40, 6, 0);
    var cheap = Station("Cheap first leg", 42, 5, 1);
    var second = Station("Second cheapest first leg", 44, 5.01, 2);
    var laterVisit = Station("Later-leg alternative", 46, 5.1, 5, 1);
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, FillPercent = 100,
      ReserveGallons = 10, Mpg = 5 };

    var selected = FuelRouteSearch.SelectCandidates([near, cheap, second, laterVisit], 400, 30, profile,
      new() { MinimumGallons = 10, TargetGallons = 10, ReplacementPriceUsd = 6 }, new());

    Assert.Contains(laterVisit, selected);
  }

  [Fact]
  public void DifferentDistantAnchorsEnterTheBudgetBeforeRepeatedCheapestDetourVariations()
  {
    var distant = Station("Dominant cheap detour", 150, 3, 25);
    var variants = Enumerable.Range(0, 9).Select(i => new List<FuelCandidate>
      { distant with { LegIndex = i % 3 }, Station($"Return {i}", 500 + i, 4, .1) }).ToList();
    var corridor = new List<FuelCandidate> { Station("Corridor", 200, 5, .1) };
    var balanced = new List<FuelCandidate> { Station("Balanced detour", 220, 3.2, 7) };
    var alternative = new List<FuelCandidate> { Station("Different detour", 240, 3.3, 12) };

    var scheduled = FuelZoneSearch.Schedule([.. variants, corridor, balanced, alternative], []);
    var firstTwelve = scheduled.Take(new FuelRegionOptions().CandidateRoadChecks).ToList();
    var repeated = scheduled.FindIndex(2, chain => chain.Any(x => x.Station.StationId == distant.Station.StationId));
    var first = scheduled.FindIndex(chain => chain.Any(x => x.Station.StationId == distant.Station.StationId));
    var second = scheduled.FindIndex(first + 1, chain => chain.Any(x => x.Station.StationId == distant.Station.StationId));

    Assert.Contains(firstTwelve, chain => chain.SequenceEqual(balanced));
    Assert.Contains(firstTwelve, chain => chain.SequenceEqual(alternative));
    Assert.True(scheduled.FindIndex(chain => chain.SequenceEqual(alternative)) < second);
    Assert.True(repeated >= 0);
    Assert.Equal(variants.Count + 4, scheduled.Count);
    Assert.All(scheduled, chain => Assert.Equal(chain.Count, chain.DistinctBy(x => x.VisitKey).Count()));
  }

  [Fact]
  public void SharedNearRoadBridgeDoesNotDeferDifferentUsefulCorridorFills()
  {
    var bridge = Station("Required bridge", 50, 6, 1);
    var first = new List<FuelCandidate> { bridge, Station("First economical fill", 200, 3, .1) };
    var second = new List<FuelCandidate> { bridge, Station("Other economical fill", 210, 3.1, .2) };
    var unrelated = new List<FuelCandidate> { Station("Distant alternate", 220, 3.2, 20) };

    var scheduled = FuelZoneSearch.Schedule([first, second, unrelated], []);

    Assert.Empty(scheduled[0]);
    Assert.Equal(unrelated, scheduled[1]);
    Assert.Equal(first, scheduled[2]);
    Assert.Equal(second, scheduled[3]);
    Assert.Equal(4, scheduled.Count);
  }

  [Fact]
  public void ReachableCheaperOutboundVisitSkipsExpensiveBridgeAndRetainsTheReturnVisit()
  {
    var bridge = Station("Expensive bridge", 50, 6, 0);
    var outbound = Station("Cheaper repeated station", 200, 3, 0);
    var turnaround = Station("Turnaround", 500, 5, 0, 1);
    var returning = outbound with { AlongMiles = 800, LegIndex = 2 };
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, FillPercent = 100,
      ReserveGallons = 10, Mpg = 5, StopCostUsd = 20 };
    var arrival = new FuelArrivalPolicy { MinimumGallons = 10, TargetGallons = 10,
      ReplacementPriceUsd = 5, EconomicPurchasesOnly = true };
    var selected = FuelRouteSearch.SelectCandidates([bridge, outbound, turnaround, returning], 900, 60,
      profile, arrival, new());
    var chains = FuelRouteSearch.Chains(selected, 900, 60, profile, arrival);
    var result = FuelOptimizer.OptimizeWithVisits(900, 60, profile, chains[0], 1, false,
      compare: false, arrivalPolicy: arrival);

    Assert.Equal(new[] { outbound.VisitKey, turnaround.VisitKey, returning.VisitKey },
      result.Purchases.Select(x => x.VisitKey));
    Assert.DoesNotContain(result.Purchases, x => x.VisitKey == bridge.VisitKey);
    Assert.Equal(100, result.Plan.Stops[0].DepartureGallons);
    Assert.All(result.Plan.Stops, x => Assert.InRange(x.ArrivalGallons, 10, 100));
    Assert.Equal(15, result.Plan.ArrivalGallons);
  }
}
