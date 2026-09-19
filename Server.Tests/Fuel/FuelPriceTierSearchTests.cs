using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPriceTierSearchTests
{
  [Fact]
  public void ProgressiveTierAddsAThreeStopCheapScaffoldButKeepsTheCheaperTotalTwoStopAlternative()
  {
    var cheap = new[]
    {
      Station("Cheap first", 100, 3),
      Station("Cheap middle", 400, 3),
      Station("Cheap final", 800, 3),
    };
    var fewerStops = new[]
    {
      Station("Slightly dearer first", 250, 3.01),
      Station("Slightly dearer final", 650, 3.01),
    };

    var chains = FuelRouteSearch.Chains(
      [.. cheap, .. fewerStops],
      1100,
      60,
      Profile(),
      Arrival()
    );
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var cheapPlan = Optimize(scheduled[1], 1100, 60);
    var fewerStopsPlan = Optimize(scheduled[2], 1100, 60);

    Assert.Equal(
      cheap.Select(x => x.VisitKey),
      scheduled[1].Select(x => x.VisitKey)
    );
    Assert.Equal(
      fewerStops.Select(x => x.VisitKey),
      scheduled[2].Select(x => x.VisitKey)
    );
    Assert.Equal(
      fewerStops.Select(x => x.VisitKey),
      chains[0].Select(x => x.VisitKey)
    );
    Assert.Equal(570, cheapPlan.EconomicCostUsd);
    Assert.Equal(551.7, fewerStopsPlan.EconomicCostUsd, 6);
    Assert.Equal(cheapPlan.PurchaseCostUsd + 3 * 20, cheapPlan.EconomicCostUsd);
    Assert.Equal(
      fewerStopsPlan.PurchaseCostUsd + 2 * 20,
      fewerStopsPlan.EconomicCostUsd,
      6
    );
  }

  [Fact]
  public void CheapestCompleteTierKeepsPurchasesAcrossTheWholeAssignedItinerary()
  {
    var cheap = new[]
    {
      Station("First cheap fill", 70, 3),
      Station("Middle cheap fill", 500, 3, leg: 1),
      Station("Final cheap fill", 900, 3, leg: 2),
    };
    var expensive = new[]
    {
      Station("First expensive fill", 50, 6),
      Station("Middle expensive fill", 450, 6, leg: 1),
      Station("Final expensive fill", 850, 6, leg: 2),
    };

    var chains = FuelRouteSearch.Chains(
      [.. expensive, .. cheap],
      1200,
      30,
      Profile(),
      Arrival()
    );
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var plan = Optimize(scheduled[1], 1200, 30);

    Assert.Empty(scheduled[0]);
    Assert.Equal(
      cheap.Select(x => x.VisitKey),
      scheduled[1].Select(x => x.VisitKey)
    );
    Assert.Equal(new[] { 0, 1, 2 }, scheduled[1].Select(x => x.LegIndex));
    Assert.Equal(1200, plan.RemainingMiles);
    Assert.Equal(10, plan.ArrivalGallons);
    Assert.Equal(plan.PurchaseCostUsd + 3 * 20, plan.EconomicCostUsd);
    Assert.All(
      plan.Stops,
      stop => Assert.InRange(stop.ArrivalGallons, 10, 100)
    );
  }

  [Fact]
  public void HigherPriceIsRestoredOnlyForTheSmallBridgeNeededToReachCheapFuel()
  {
    var bridge = Station("Required bridge", 70, 5);
    var cheap = Station("Main cheap fill", 200, 2);
    var later = Station("Unneeded intermediate price", 400, 3);
    var expensive = Station("Unneeded expensive fill", 90, 6);

    var chains = FuelRouteSearch.Chains(
      [cheap, later, expensive, bridge],
      500,
      30,
      Profile(),
      Arrival()
    );
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var plan = Optimize(scheduled[1], 500, 30);

    Assert.Equal(
      new[] { bridge.VisitKey, cheap.VisitKey },
      scheduled[1].Select(x => x.VisitKey)
    );
    Assert.Equal(25, plan.Stops[0].BuyGallons);
    Assert.False(plan.Stops[0].FillToTarget);
    Assert.Equal(15, plan.Stops[1].ArrivalGallons);
    Assert.Equal(15, plan.ArrivalGallons);
    Assert.Equal(plan.PurchaseCostUsd + 2 * 20, plan.EconomicCostUsd);
  }

  [Fact]
  public void CheapOutboundAndReturnVisitsRemainDistinctWhenAHigherPriceMiddleBridgeIsRequired()
  {
    var outbound = Station("Cheap repeated station", 200, 3);
    var returning = outbound with { AlongMiles = 800, LegIndex = 2 };
    var middle = Station("Required middle bridge", 500, 5, leg: 1);
    var unneeded = Station("Unneeded first bridge", 50, 6);

    var chains = FuelRouteSearch.Chains(
      [unneeded, outbound, middle, returning],
      900,
      60,
      Profile(),
      Arrival()
    );
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var plan = Optimize(scheduled[1], 900, 60);

    Assert.Equal(
      new[] { outbound.VisitKey, middle.VisitKey, returning.VisitKey },
      scheduled[1].Select(x => x.VisitKey)
    );
    Assert.Equal(100, plan.Stops[0].DepartureGallons);
    Assert.Equal(
      2,
      plan.Stops.Count(x => x.StationId == outbound.Station.StationId)
    );
    Assert.All(
      scheduled,
      chain =>
        Assert.Equal(chain.Count, chain.DistinctBy(x => x.VisitKey).Count())
    );
    Assert.Equal(15, plan.ArrivalGallons);
  }

  [Fact]
  public void TwentyDollarStopCostStillRejectsATinyDiscountAfterTheRequiredBridge()
  {
    var bridge = Station("Required first fill", 80, 3);
    var tinyDiscount = Station("Unnecessary top-up", 170, 2.99);

    var chains = FuelRouteSearch.Chains(
      [tinyDiscount, bridge],
      350,
      30,
      Profile(),
      Arrival()
    );
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var plan = Optimize(scheduled[1], 350, 30);

    Assert.Equal(bridge.VisitKey, Assert.Single(scheduled[1]).VisitKey);
    Assert.Equal(bridge.Station.StationId, Assert.Single(plan.Stops).StationId);
    Assert.Equal(plan.PurchaseCostUsd + 20, plan.EconomicCostUsd);
  }

  [Fact]
  public void CheapPriceIsCheckedFirstButNearRoadAlternativeCanWinAfterActualDrivingCost()
  {
    var cheap = Station("Small distant discount", 100, 2.99, access: 20);
    var nearby = Station("Nearby", 100, 3, access: .1);
    var chains = FuelRouteSearch.Chains(
      [cheap, nearby],
      300,
      40,
      Profile(),
      Arrival()
    );

    var scheduled = FuelZoneSearch.Schedule(chains, []);
    var firstChecks = scheduled
      .Take(new FuelRegionOptions().CandidateRoadChecks)
      .ToList();
    var cheapPlan = Optimize(scheduled[1], 300, 40);
    var nearbyPlan = Optimize(scheduled[2], 300, 40);
    var impact = new FuelScheduleImpact(
      DateTime.UnixEpoch,
      true,
      true,
      false,
      false,
      60,
      0,
      [],
      null
    );
    var checkedCheapCost =
      cheapPlan.EconomicCostUsd
      + FuelScheduleRanking.DelayCost(
        impact,
        0,
        60,
        Profile().DriverHourlyCostUsd
      );

    Assert.Empty(firstChecks[0]);
    Assert.Equal(cheap.VisitKey, Assert.Single(firstChecks[1]).VisitKey);
    Assert.Equal(nearby.VisitKey, Assert.Single(firstChecks[2]).VisitKey);
    Assert.True(cheapPlan.EconomicCostUsd < nearbyPlan.EconomicCostUsd);
    Assert.True(checkedCheapCost > nearbyPlan.EconomicCostUsd);
    Assert.Equal(110, nearbyPlan.EconomicCostUsd);
  }

  [Fact]
  public void ProtectedNearestComparisonIsNotDeferredForARepeatedDistantPhysicalAnchor()
  {
    var cheapest = Station("Shared anchor", 100, 2, access: 20);
    var nearerVisit = cheapest with
    {
      AlongMiles = 700,
      LegIndex = 2,
      ExtraInMiles = 6,
    };
    List<FuelCandidate> cheapChain = [cheapest];
    List<FuelCandidate> nearestChain =
    [
      Station("Near bridge", 200, 3, access: .1),
      nearerVisit,
    ];

    var scheduled = FuelZoneSearch.Schedule([cheapChain, nearestChain], []);

    Assert.Equal(cheapChain, scheduled[1]);
    Assert.Equal(nearestChain, scheduled[2]);
    Assert.Equal(3, scheduled.Count);
  }

  private static FuelPlan Optimize(
    List<FuelCandidate> chain,
    double miles,
    double gallons
  ) =>
    FuelOptimizer.Optimize(
      miles,
      gallons,
      Profile(),
      chain
        .Select(x => x with { ExtraInMiles = 0, ExtraOutMiles = 0 })
        .ToList(),
      1,
      false,
      compare: false,
      arrivalPolicy: Arrival()
    );

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
      DriverHourlyCostUsd = 35,
    };

  private static FuelArrivalPolicy Arrival() =>
    new()
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 5,
      EconomicPurchasesOnly = true,
    };

  private static FuelCandidate Station(
    string name,
    double mile,
    double price,
    double access = 0,
    int leg = 0
  ) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = name,
        Point = new(40, -80),
        YourPrice = price,
        EconomicPrice = price,
        Currency = "USD",
        Unit = "US gal",
      },
      mile,
      access,
      0,
      price,
      price
    )
    {
      LegIndex = leg,
    };
}
