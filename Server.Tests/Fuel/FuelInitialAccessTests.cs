using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelInitialAccessTests
{
  [Fact]
  public void InitialAccessConsumesFuelBeforeTheFirstPurchaseWithoutChangingTheReportedLevel()
  {
    var profile = Profile();
    var station = Station(10);
    var policy = new FuelArrivalPolicy
    {
      MinimumGallons = 10,
      TargetGallons = 100.5,
      ReplacementPriceUsd = 6,
      EconomicPurchasesOnly = true,
    };

    var plan = FuelOptimizer.Optimize(
      50,
      30,
      profile,
      [station],
      1,
      false,
      arrivalPolicy: policy,
      initialAccessMiles: 5
    );

    var purchase = Assert.Single(plan.Stops);
    Assert.Equal(30, plan.StartingGallons);
    Assert.Equal(55, plan.RemainingMiles);
    Assert.Equal(27, purchase.ArrivalGallons);
    Assert.Equal(73.5, purchase.BuyGallons);
    Assert.Equal(100.5, purchase.DepartureGallons);
    Assert.True(purchase.FillToTarget);
    Assert.Equal(92.5, plan.ArrivalGallons);
    Assert.Equal(73.5 * 2 + 20, plan.EconomicCostUsd);
    Assert.Equal(0, plan.ExtraMinutes);
    var comparison = FuelOptimizer.Optimize(
      50,
      30,
      profile,
      [station],
      1,
      false,
      fewestStops: true,
      compare: false,
      arrivalPolicy: policy,
      initialAccessMiles: 5
    );
    Assert.Equal(
      comparison.EconomicCostUsd
        + comparison.ExpectedFutureFuelCostUsd
        - plan.EconomicCostUsd
        - plan.ExpectedFutureFuelCostUsd,
      plan.SavingsUsd!.Value,
      8
    );
  }

  [Fact]
  public void DirectArrivalPaysInitialAccessOnlyOnce()
  {
    var (plan, visits) = FuelOptimizer.OptimizeWithVisits(
      50,
      30,
      Profile(),
      [],
      1,
      false,
      initialAccessMiles: 10
    );

    Assert.Empty(visits);
    Assert.Empty(plan.Stops);
    Assert.Equal(30, plan.StartingGallons);
    Assert.Equal(60, plan.RemainingMiles);
    Assert.Equal(18, plan.ArrivalGallons);
    Assert.Equal(0, plan.PurchaseGallons);
    Assert.Equal(0, plan.EconomicCostUsd);
  }

  [Fact]
  public void StartingAtReserveCanReachFirstPurchaseUsingReserve()
  {
    var plan = FuelOptimizer.Optimize(
      20,
      10,
      Profile(),
      [Station(0)],
      1,
      false,
      initialAccessMiles: 5
    );
    Assert.Equal(9, Assert.Single(plan.Stops).ArrivalGallons);
    Assert.True(plan.ArrivalGallons >= 10);
  }

  [Fact]
  public void AlreadyDepletedReserveCanReachFirstPurchaseButNotInventInitialFuel()
  {
    var profile = Profile();
    var recovered = FuelOptimizer.Optimize(
      20,
      1,
      profile,
      [Station(0)],
      1,
      false,
      initialAccessMiles: 5
    );

    var purchase = Assert.Single(recovered.Stops);
    Assert.Equal(0, purchase.ArrivalGallons);
    Assert.True(purchase.DepartureGallons >= profile.ReserveGallons);
    Assert.True(recovered.ArrivalGallons >= profile.ReserveGallons);
    Assert.Throws<RoutePlanningException>(
      () =>
        FuelOptimizer.Optimize(
          20,
          1,
          profile,
          [Station(0)],
          1,
          false,
          initialAccessMiles: 5.1
        )
    );
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  public void InvalidInitialAccessIsRejected(double access)
  {
    Assert.Throws<RoutePlanningException>(
      () =>
        FuelOptimizer.Optimize(
          50,
          30,
          Profile(),
          [],
          1,
          false,
          initialAccessMiles: access
        )
    );
    Assert.Throws<RoutePlanningException>(
      () =>
        FuelOptimizer.OptimizeWithVisits(
          50,
          30,
          Profile(),
          [],
          1,
          false,
          initialAccessMiles: access
        )
    );
  }

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 100.5,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
      DriverHourlyCostUsd = 35,
    };

  private static FuelCandidate Station(double miles) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Fuel",
        Point = new(40, -80),
        YourPrice = 2,
        EconomicPrice = 2,
        Currency = "USD",
        Unit = "US gal",
      },
      miles,
      0,
      0,
      2,
      2
    )
    {
      LegIndex = 0,
    };
}
