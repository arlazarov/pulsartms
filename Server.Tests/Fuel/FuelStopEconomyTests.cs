using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelStopEconomyTests
{
  [Theory]
  [InlineData(0, 1, false)]
  [InlineData(.86, 1, false)]
  [InlineData(19.99, 1, false)]
  [InlineData(20, 1, true)]
  [InlineData(20.01, 1, true)]
  [InlineData(39.99, 2, false)]
  [InlineData(40, 2, true)]
  [InlineData(40.01, 2, true)]
  public void AdditionalStopsMustEarnTwentyDollarsEach(
    double savings,
    int additional,
    bool worthwhile
  )
  {
    var comparison = FuelStopEconomy.Compare(
      1000 - savings,
      2 + additional,
      1000,
      2
    );
    Assert.Equal(worthwhile, comparison < 0);
    Assert.Equal(
      -comparison,
      FuelStopEconomy.Compare(1000, 2, 1000 - savings, 2 + additional)
    );
  }

  [Fact]
  public void EqualStopCountsCompareOnlyActualCost()
  {
    Assert.True(FuelStopEconomy.Compare(999.99, 4, 1000, 4) < 0);
    Assert.True(FuelStopEconomy.Compare(1000.01, 4, 1000, 4) > 0);
    Assert.Equal(0, FuelStopEconomy.Compare(1000, 4, 1000, 4));
  }

  [Fact]
  public void Captured11006FillsAtSecondStopAndOmitsSubDollarThirdStopWithoutChargingAStopFee()
  {
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 250,
      FillPercent = 100,
      Mpg = 6.720416657142858,
      ReserveGallons = 25,
      StopCostUsd = 0,
      DriverHourlyCostUsd = 35,
    };
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 125,
      TargetGallons = 250,
      ReplacementPriceUsd = 5.653,
      PoorArea = true,
      EconomicPurchasesOnly = true,
    };
    var candidates = new[]
    {
      Station("365", 484.8903756814759, 5.747),
      Station("310", 913.9153607712367, 5.299),
      Station("723", 1050.7292788242412, 5.292),
      Station("361", 1765.7926260392896, 5.419),
      Station("399", 3124.4236898595336, 5.688),
    };
    const double miles = 3818.3899775312175;
    var chains = FuelRouteSearch.Chains(
      candidates,
      miles,
      102.5,
      profile,
      arrival,
      includeAccess: true,
      initialAccessMiles: .5
    );
    var best = Optimize(chains[0]);
    var cheapest = Optimize(candidates);

    Assert.Equal(
      new[] { "365", "310", "361", "399" },
      best.Stops.Select(stop => stop.Name)
    );
    Assert.True(best.Stops[1].FillToTarget);
    Assert.Equal(223.78847845285716, best.Stops[1].BuyGallons, 7);
    Assert.Equal(250, best.Stops[1].DepartureGallons);
    Assert.Equal(134.34970236425576, best.ArrivalGallons, 7);
    Assert.Equal(cheapest.ArrivalGallons, best.ArrivalGallons, 7);
    Assert.InRange(best.EconomicCostUsd - cheapest.EconomicCostUsd, .85, .87);
    Assert.Equal(best.PurchaseCostUsd + 16d / 60 * 35, best.EconomicCostUsd, 7);
    Assert.All(
      best.Stops,
      stop => Assert.InRange(stop.ArrivalGallons, 25, 250)
    );
    Assert.True(best.ArrivalGallons >= arrival.MinimumGallons);
    var scheduled = FuelZoneSearch.Schedule(chains, []);
    Assert.Contains(
      scheduled.Take(12),
      chain => chain.SequenceEqual(chains[0])
    );
    var originalScore = best.EconomicCostUsd;
    Assert.True(
      FuelStopEconomy.Compare(
        best.EconomicCostUsd + best.ExpectedFutureFuelCostUsd,
        best.Stops.Count,
        cheapest.EconomicCostUsd + cheapest.ExpectedFutureFuelCostUsd,
        cheapest.Stops.Count
      ) < 0
    );
    Assert.Equal(originalScore, best.EconomicCostUsd);

    FuelPlan Optimize(IReadOnlyList<FuelCandidate> chain) =>
      FuelOptimizer.Optimize(
        miles,
        102.5,
        profile,
        chain,
        1,
        false,
        compare: false,
        arrivalPolicy: arrival,
        initialAccessMiles: .5
      );
  }

  // Truck 54777, from the staged preview of 2026-09-22. The saved plan buys
  // at #856 and #706; #480 is cheaper than #706 but sits ten miles off the
  // road. It cannot replace #706 - filling there leaves 113 gallons at the
  // end, under the 125-gallon arrival minimum - so the question is whether
  // it is worth a third stop. It is found, its quantities are re-optimized,
  // and both plans end with the same fuel in the tank; it saves 26 cents.
  [Theory]
  [InlineData(5.747, new[] { "856", "706" })]
  [InlineData(5.55, new[] { "856", "480", "706" })]
  public void Captured54777AddsAThirdStopOnlyWhenItsDiscountClearsTheThreshold(
    double detouredPrice,
    string[] expected
  )
  {
    const double miles = 2690.377;
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 250,
      FillPercent = 100,
      Mpg = 6.720416657,
      ReserveGallons = 25,
      StopCostUsd = 0,
      DriverHourlyCostUsd = 35,
    };
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 125,
      TargetGallons = 250,
      ReplacementPriceUsd = 6.131,
    };
    var candidates = new[]
    {
      Station("856", 1212.888, 5.645),
      Detoured("480", 1781.402, detouredPrice),
      Station("706", 2119.886, 6.015),
    };
    var chains = FuelRouteSearch.Chains(
      candidates,
      miles,
      242.5,
      profile,
      arrival,
      includeAccess: true
    );
    FuelPlan Optimize(IReadOnlyList<FuelCandidate> chain) =>
      FuelOptimizer.Optimize(
        miles,
        242.5,
        profile,
        chain,
        1,
        false,
        compare: false,
        arrivalPolicy: arrival
      );
    var named = chains
      .Select(chain => chain.Select(x => x.Station.Name).ToArray())
      .ToList();

    // Both are reached: an extra stop is not outside the search.
    Assert.Contains(named, chain => chain.SequenceEqual(["856", "706"]));
    Assert.Contains(named, chain => chain.SequenceEqual(["856", "480", "706"]));
    Assert.Equal(expected, named[0]);

    var two = Optimize(chains.First(c => c.Count == 2));
    var three = Optimize(chains.First(c => c.Count == 3));
    var twoTotal = two.EconomicCostUsd + two.ExpectedFutureFuelCostUsd;
    var threeTotal = three.EconomicCostUsd + three.ExpectedFutureFuelCostUsd;

    // Same fuel in the tank at the end, so neither plan is cheaper merely
    // by arriving emptier.
    Assert.Equal(two.ArrivalGallons, three.ArrivalGallons, 7);
    Assert.True(three.ArrivalGallons >= arrival.MinimumGallons);

    // The last purchase is the exact headroom, not a rounded step: the
    // gallons a dearer station sells are only the ones the cheaper one
    // could not carry.
    Assert.True(three.Stops[^1].FillToTarget);
    Assert.Equal(250, three.Stops[^1].DepartureGallons, 7);
    Assert.Equal(
      250 - three.Stops[^1].ArrivalGallons,
      three.Stops[^1].BuyGallons,
      7
    );
    Assert.True(three.Stops[1].FillToTarget);

    // The threshold decides, and it decides on the complete horizon.
    Assert.Equal(
      expected.Length == 2,
      FuelStopEconomy.Compare(threeTotal, 3, twoTotal, 2) > 0
    );
    if (expected.Length == 2)
      Assert.InRange(
        twoTotal - threeTotal,
        0,
        FuelStopEconomy.MinimumSavingsUsd
      );
    else
      Assert.True(twoTotal - threeTotal >= FuelStopEconomy.MinimumSavingsUsd);
  }

  private static FuelCandidate Detoured(
    string name,
    double mile,
    double price
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
        DetourMinutes = 22.828,
      },
      mile,
      5.207,
      5.207,
      price,
      price
    );

  private static FuelCandidate Station(
    string name,
    double mile,
    double price
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
        DetourMinutes = 4,
      },
      mile,
      .5,
      .5,
      price,
      price
    );
}
