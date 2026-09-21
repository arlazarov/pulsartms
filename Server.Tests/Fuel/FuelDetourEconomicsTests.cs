using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelDetourEconomicsTests
{
  [Theory]
  [InlineData(0, 2.99)]
  [InlineData(1, 2.1)]
  [InlineData(4.9, 2.1)]
  [InlineData(5, 2.1)]
  public void OneHourDetourCannotWinForTinySavingsEvenWhenExtraDistanceIsShort(
    double extraMiles,
    double price
  )
  {
    var profile = Profile();
    var closer = Station("On route", 100, 3);
    var cheaper = Station("Small discount", 100, price, extraMiles, 60);
    var withoutTimeCost = FuelOptimizer.Optimize(
      300,
      40,
      Profile(hourlyCost: 0),
      [closer, cheaper],
      1,
      false
    );
    Assert.Equal("Small discount", Assert.Single(withoutTimeCost.Stops).Name);
    var plan = FuelOptimizer.Optimize(
      300,
      40,
      profile,
      [closer, cheaper],
      1,
      false
    );
    Assert.Equal("On route", Assert.Single(plan.Stops).Name);
    Assert.Equal(110, plan.EconomicCostUsd);
    Assert.True(plan.ArrivalGallons >= profile.ReserveGallons);
  }

  [Fact]
  public void ALongerDetourWinsWhenSavingsCoverFuelTimeAndTheStopCost()
  {
    var profile = Profile();
    var closer = Station("On route", 100, 3);
    var worthwhile = Station("Worthwhile discount", 100, 1.3, 4.9, 60);
    var direct = FuelOptimizer.Optimize(300, 40, profile, [closer], 1, false);
    var plan = FuelOptimizer.Optimize(
      300,
      40,
      profile,
      [closer, worthwhile],
      1,
      false
    );
    var purchase = Assert.Single(plan.Stops);
    Assert.Equal("Worthwhile discount", purchase.Name);
    Assert.Equal(40, purchase.BuyGallons);
    Assert.Equal(107, plan.EconomicCostUsd);
    Assert.True(plan.EconomicCostUsd < direct.EconomicCostUsd);
    Assert.Equal(plan.PurchaseCostUsd + 20 + 35, plan.EconomicCostUsd);
    Assert.Equal(20, profile.StopCostUsd);
  }

  [Fact]
  public void TheExistingTwentyDollarStopCostStillAvoidsAnUnnecessaryExtraPurchase()
  {
    var profile = Profile();
    var plan = FuelOptimizer.Optimize(
      350,
      30,
      profile,
      [Station("First", 80, 3), Station("Tiny discount", 170, 2.99)],
      1,
      false
    );
    Assert.Equal("First", Assert.Single(plan.Stops).Name);
    Assert.Equal(plan.PurchaseCostUsd + 20, plan.EconomicCostUsd);
    Assert.Equal(20, profile.StopCostUsd);
  }

  [Fact]
  public void NegativeEstimatedAccessTimeCannotCreateAnEconomicCredit()
  {
    var station = Station("Station", 100, 3, minutes: -60);
    var plan = FuelOptimizer.Optimize(300, 40, Profile(), [station], 1, false);
    Assert.Equal(plan.PurchaseCostUsd + 20, plan.EconomicCostUsd);
  }

  private static TruckRouteProfile Profile(double hourlyCost = 35) =>
    new()
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
      DriverHourlyCostUsd = hourlyCost,
    };

  private static FuelCandidate Station(
    string name,
    double mile,
    double price,
    double detour = 0,
    double minutes = 0
  ) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = name,
        YourPrice = price,
        EconomicPrice = price,
        Currency = "USD",
        Unit = "US gal",
        DetourMinutes = minutes,
      },
      mile,
      detour / 2,
      detour / 2,
      price,
      price
    );
}
