using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPurchaseRoundingTests
{
  [Fact]
  public void FleetPolicyRemovesLegacyStopFeeAndSelectsTheBridgeToCheaperMainFuel()
  {
    var profile = Profile();
    profile.Mpg = 6.720416657142858;
    FleetFuelDefaults.Apply(new() { StopCostUsd = 20 }).ApplyTo(profile);
    var earlier = Station("LOVES 435", 578.2446672574085, 5.604, .5);
    var cheaper = Station("LOVES 790", 959.2125527693242, 5.543, .5);
    var arrival = new FuelArrivalPolicy { MinimumGallons = 37, TargetGallons = 250,
      ReplacementPriceUsd = 5.696, EconomicPurchasesOnly = true };
    var plan = Calculate([earlier, cheaper]);
    var single = Calculate([earlier]);

    Assert.Equal(0, profile.StopCostUsd);
    Assert.Equal(new[] { "LOVES 435", "LOVES 790" }, plan.Stops.Select(x => x.Name));
    Assert.Equal(30, plan.Stops[0].BuyGallons);
    Assert.True(plan.Stops[1].FillToTarget);
    Assert.Equal(250, plan.Stops[1].DepartureGallons, 9);
    Assert.Equal(plan.PurchaseCostUsd + 8d / 60 * 35, plan.EconomicCostUsd, 9);
    Assert.Equal(15.648311353, single.EconomicCostUsd + single.ExpectedFutureFuelCostUsd
      - plan.EconomicCostUsd - plan.ExpectedFutureFuelCostUsd, 6);
    Assert.All(plan.Stops, stop => Assert.True(stop.ArrivalGallons >= 25));
    Assert.True(plan.ArrivalGallons >= 37);

    FuelPlan Calculate(List<FuelCandidate> candidates) => FuelOptimizer.Optimize(1450.0298828629668,
      140, profile, candidates, 1, false, compare: false, arrivalPolicy: arrival);
  }

  [Fact]
  public void EqualAccessStationsCannotFavorTheDearerPriceThroughPerLegRounding()
  {
    var profile = Profile();
    profile.Mpg = 6.720416657142858;
    var cheaper = Station("LOVES 682", 67.52974230843016, 5.745, .5);
    var dearer = Station("LOVES 366", 159.689649687653, 5.924, .5);
    var arrival = new FuelArrivalPolicy { MinimumGallons = 125, TargetGallons = 250,
      ReplacementPriceUsd = 5.734, PoorArea = true, EconomicPurchasesOnly = true };
    var cheapPlan = Calculate([cheaper]);
    var dearPlan = Calculate([dearer]);
    var combined = Calculate([cheaper, dearer]);

    Assert.Equal("LOVES 682", Assert.Single(combined.Stops).Name);
    Assert.Equal(25, Assert.Single(cheapPlan.Stops).BuyGallons);
    Assert.Equal(25, Assert.Single(dearPlan.Stops).BuyGallons);
    Assert.Equal(cheapPlan.ArrivalGallons, dearPlan.ArrivalGallons, 9);
    Assert.Equal(175 + 25 - (452.051517 + 1) / profile.Mpg.Value, combined.ArrivalGallons, 9);
    Assert.Equal(25 * (5.924 - 5.745),
      dearPlan.EconomicCostUsd + dearPlan.ExpectedFutureFuelCostUsd
      - cheapPlan.EconomicCostUsd - cheapPlan.ExpectedFutureFuelCostUsd, 9);
    var chains = FuelRouteSearch.Chains([cheaper, dearer], 452.051517, 175, profile, arrival, includeAccess: true);
    Assert.Equal(cheaper.VisitKey, Assert.Single(chains[0]).VisitKey);

    FuelPlan Calculate(List<FuelCandidate> candidates) => FuelOptimizer.Optimize(452.051517, 175, profile,
      candidates, 1, false, compare: false, arrivalPolicy: arrival);
  }

  [Theory]
  [InlineData(18, 25)]
  [InlineData(25, 25)]
  [InlineData(25.1, 30)]
  [InlineData(31, 40)]
  [InlineData(91, 100)]
  [InlineData(99, 100)]
  [InlineData(100, 100)]
  public void PartialPurchasesRoundUpBeforeCostAndArrivalAreCalculated(double required, double expected)
  {
    var profile = Profile();
    var miles = (100 + required - profile.ReserveGallons) * profile.Mpg!.Value;
    var plan = FuelOptimizer.Optimize(miles, 100, profile, [Station("Purchase", 1, 5)], 1, false, compare: false);
    var stop = Assert.Single(plan.Stops);
    Assert.False(stop.FillToTarget);
    Assert.Equal(expected, stop.BuyGallons);
    Assert.Equal(expected * 5, plan.PurchaseCostUsd);
    Assert.Equal(expected * 5 + profile.StopCostUsd, plan.EconomicCostUsd);
    Assert.Equal(profile.ReserveGallons + expected - required, plan.ArrivalGallons, 9);
    Assert.True(plan.ArrivalGallons >= profile.ReserveGallons);
  }

  [Fact]
  public void RoundedPurchaseCannotOverfillAndFullTargetIsNotRoundedAboveCapacity()
  {
    var profile = Profile();
    profile.TankGallons = 211.33764188651872;
    var plan = FuelOptimizer.Optimize(100, 114.7, profile, [Station("Full", 1, 2)], 1, false, compare: false,
      arrivalPolicy: new() { MinimumGallons = 25, TargetGallons = profile.TankGallons.Value,
        ReplacementPriceUsd = 6, EconomicPurchasesOnly = true });
    var stop = Assert.Single(plan.Stops);
    Assert.True(stop.FillToTarget);
    Assert.Equal(profile.TankGallons.Value, stop.DepartureGallons, 9);
    Assert.Equal(profile.TankGallons.Value - (114.7 - 1 / profile.Mpg!.Value), stop.BuyGallons, 9);
    Assert.True(stop.BuyGallons < 100);
    Assert.Equal(stop.BuyGallons * 2, plan.PurchaseCostUsd, 9);
  }

  [Theory]
  [InlineData(500)]
  [InlineData(2000)]
  public void FullShortlistWithFractionalAccessKeepsAFeasiblePlan(double miles)
  {
    var profile = Profile();
    profile.Mpg = 6.720416657142858;
    var stations = Enumerable.Range(1, 24).Select(i => Station($"Station {i}", miles * i / 25,
      4 + i % 7 * .031, .5 + i % 11 * .137)).ToList();
    var plan = FuelOptimizer.Optimize(miles, 175.3, profile, stations, 1, false, compare: false,
      arrivalPolicy: new() { MinimumGallons = 125, TargetGallons = 250,
        ReplacementPriceUsd = 4.2, EconomicPurchasesOnly = true });
    Assert.True(plan.ArrivalGallons >= 125);
    Assert.All(plan.Stops, stop => Assert.True(stop.BuyGallons >= 25));
    Assert.Equal(175.3 + plan.PurchaseGallons - (miles + plan.Stops.Sum(x => x.DetourMiles)) / profile.Mpg.Value,
      plan.ArrivalGallons, 8);
  }

  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 250, Mpg = 5,
    ReserveGallons = 25, FillPercent = 100, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
  private static FuelCandidate Station(string name, double miles, double price, double access = 0) => new(new()
    { StationId = Guid.NewGuid(), Name = name, Point = new(40, -80), YourPrice = price, EconomicPrice = price,
      Currency = "USD", Unit = "US gal", DetourMinutes = access > 0 ? 4 : 0 }, miles, access, access, price, price);
}
