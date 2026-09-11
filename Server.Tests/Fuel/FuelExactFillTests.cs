using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelExactFillTests
{
  [Theory]
  [InlineData(100)]
  [InlineData(90)]
  public void FullPurchaseReachesExactConfiguredTargetAndChargesItsEntireQuantity(double fillPercent)
  {
    var profile = Profile(211.33764188651872);
    profile.FillPercent = fillPercent;
    profile.ReserveGallons = 25;
    var target = profile.TankGallons!.Value * (fillPercent / 100);
    var station = Station("Full fill", 10, 3) with { EconomicPriceUsd = 2.5 };
    var plan = FuelOptimizer.Optimize(100, 36, profile, [station], 1, true, compare: false,
      arrivalPolicy: Arrival(profile, target, 6));

    var purchase = Assert.Single(plan.Stops);
    Assert.True(purchase.FillToTarget);
    Assert.Equal(34, purchase.ArrivalGallons);
    Assert.Equal(target, purchase.DepartureGallons);
    Assert.Equal(target - 34, purchase.BuyGallons, 10);
    Assert.Equal(purchase.DepartureGallons, purchase.ArrivalGallons + purchase.BuyGallons, 10);
    Assert.Equal(fillPercent, purchase.DepartureGallons / profile.TankGallons.Value * 100, 10);
    Assert.True(purchase.DepartureGallons <= profile.TankGallons.Value);
    Assert.Equal(target - 18, plan.ArrivalGallons, 10);
    Assert.Equal(purchase.BuyGallons, plan.PurchaseGallons);
    Assert.Equal(purchase.BuyGallons * 3, plan.PurchaseCostUsd, 9);
    Assert.Equal(purchase.BuyGallons * 2.5 + 20, plan.EconomicCostUsd, 9);
    Assert.Equal(18 * 6, plan.ExpectedFutureFuelCostUsd, 9);
  }

  [Fact]
  public void FractionalFuelSurvivesUntilLaterFullFillWithoutBeingBoughtTwice()
  {
    var profile = Profile(211.33764188651872);
    profile.ReserveGallons = 25;
    var target = profile.TankGallons!.Value;
    var plan = FuelOptimizer.Optimize(1000, 36, profile,
      [Station("Cheaper first fill", 10, 2), Station("Later fill", 910, 3)], 1, false, compare: false,
      arrivalPolicy: Arrival(profile, target, 6));

    Assert.Equal(2, plan.Stops.Count);
    Assert.All(plan.Stops, purchase => Assert.True(purchase.FillToTarget));
    Assert.Equal(target, plan.Stops[0].DepartureGallons);
    Assert.Equal(target - 180, plan.Stops[1].ArrivalGallons, 10);
    Assert.Equal(180, plan.Stops[1].BuyGallons);
    Assert.Equal(target, plan.Stops[1].DepartureGallons);
    Assert.Equal(target - 34 + 180, plan.PurchaseGallons, 10);
    Assert.Equal((target - 34) * 2 + 180 * 3, plan.PurchaseCostUsd, 9);
    Assert.Equal(plan.PurchaseCostUsd + 40, plan.EconomicCostUsd, 9);
    Assert.Equal(target - 18, plan.ArrivalGallons, 10);
  }

  [Fact]
  public void RequiredSmallBridgeStaysPartialBeforeExactCheaperFullFill()
  {
    var profile = Profile(100.5);
    var plan = FuelOptimizer.Optimize(500, 30, profile,
      [Station("Expensive bridge", 70, 6), Station("Cheaper full fill", 200, 2)], 1, false, compare: false,
      arrivalPolicy: Arrival(profile, 100.5, 6));

    Assert.Equal(2, plan.Stops.Count);
    Assert.False(plan.Stops[0].FillToTarget);
    Assert.Equal(25, plan.Stops[0].BuyGallons);
    Assert.Equal(41, plan.Stops[0].DepartureGallons);
    Assert.True(plan.Stops[1].FillToTarget);
    Assert.Equal(15, plan.Stops[1].ArrivalGallons);
    Assert.Equal(85.5, plan.Stops[1].BuyGallons);
    Assert.Equal(100.5, plan.Stops[1].DepartureGallons);
    Assert.Equal(40.5, plan.ArrivalGallons);
    Assert.Equal(plan.PurchaseCostUsd + 40, plan.EconomicCostUsd);
  }

  [Fact]
  public void PartialPurchaseAfterFullFillKeepsTheFractionWithoutChangingItsWholeGallons()
  {
    var profile = Profile(100.5);
    var plan = FuelOptimizer.Optimize(720, 30, profile,
      [Station("First full fill", 70, 1), Station("Later partial fill", 500, 5)], 1, false, compare: false,
      arrivalPolicy: Arrival(profile, 10, 5));

    Assert.Equal(2, plan.Stops.Count);
    Assert.True(plan.Stops[0].FillToTarget);
    Assert.Equal(100.5, plan.Stops[0].DepartureGallons);
    Assert.False(plan.Stops[1].FillToTarget);
    Assert.Equal(14.5, plan.Stops[1].ArrivalGallons);
    Assert.Equal(40, plan.Stops[1].BuyGallons);
    Assert.Equal(54.5, plan.Stops[1].DepartureGallons);
    Assert.Equal(10.5, plan.ArrivalGallons);
    Assert.All(plan.Stops, purchase => Assert.Equal(purchase.DepartureGallons,
      purchase.ArrivalGallons + purchase.BuyGallons));
  }

  [Fact]
  public void FractionCannotInventEnoughFuelToBreakTheReserve()
  {
    var profile = Profile(100.9);
    Assert.Throws<RoutePlanningException>(() => FuelOptimizer.Optimize(455, 20, profile,
      [Station("Current fuel station", 0, 1)], 1, false, compare: false,
      arrivalPolicy: Arrival(profile, 10, 5)));
  }

  private static TruckRouteProfile Profile(double tank) => new() { Confirmed = true, TankGallons = tank, Mpg = 5,
    ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
  private static FuelArrivalPolicy Arrival(TruckRouteProfile profile, double target, double price) => new()
    { MinimumGallons = profile.ReserveGallons, TargetGallons = target, ReplacementPriceUsd = price, EconomicPurchasesOnly = true };
  private static FuelCandidate Station(string name, double miles, double price) => new(new()
    { StationId = Guid.NewGuid(), Name = name, Point = new(40, -80), YourPrice = price,
      EconomicPrice = price, Unit = "US gal", Currency = "USD" }, miles, 0, 0, price, price) { LegIndex = 0 };
}
