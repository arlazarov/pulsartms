using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelLowLevelRecoveryTests
{
  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 100,
    FillPercent = 100, ReserveGallons = 10, Mpg = 5, StopCostUsd = 20 };
  private static FuelCandidate Station(string name, double miles, double price) =>
    new(new() { StationId = Guid.NewGuid(), Name = name, YourPrice = price }, miles, 0, 0, price, price);
  private static FuelArrivalPolicy Arrival() => new() { MinimumGallons = 10, TargetGallons = 10,
    ReplacementPriceUsd = 3, EconomicPurchasesOnly = true };

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  [InlineData(10)]
  public void PositiveBelowReserveFuelCanReachOnlyItsFirstPurchaseWithoutReserve(double firstMiles)
  {
    var profile = Profile();
    Assert.Null(profile.Validate(true));
    var nearby = Station("Nearby recovery", firstMiles, 4);

    var plan = FuelOptimizer.Optimize(100, 2, profile, [nearby], 1, false,
      compare: false, arrivalPolicy: Arrival());

    var stop = Assert.Single(plan.Stops);
    Assert.Equal(2, plan.StartingGallons);
    Assert.InRange(stop.ArrivalGallons, 0, 2);
    Assert.InRange(stop.DepartureGallons, 10, 100);
    Assert.Equal(30, stop.BuyGallons);
    Assert.Equal(12, plan.ArrivalGallons);
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(0, 5)]
  [InlineData(2, 11)]
  [InlineData(-1, 0)]
  [InlineData(101, 0)]
  public void EmptyInvalidOrPhysicallyUnreachableFuelCannotProduceADriveablePlan(double gallons, double miles)
  {
    Assert.Throws<RoutePlanningException>(() => FuelOptimizer.Optimize(100, gallons, Profile(),
      [Station("Cannot reach", miles, 3)], 1, false, compare: false, arrivalPolicy: Arrival()));
  }

  [Fact]
  public void ActualAccessDistanceCanMakeTheFirstStationUnreachable()
  {
    var station = Station("Road entrance beyond range", 5, 3) with { ExtraInMiles = 6 };
    Assert.Throws<RoutePlanningException>(() => FuelOptimizer.Optimize(100, 2, Profile(), [station],
      1, false, compare: false, arrivalPolicy: Arrival()));
  }

  [Theory]
  [InlineData(10)]
  [InlineData(12)]
  public void StartingAtOrAboveReserveStillMustReachTheFirstPurchaseWithReserve(double gallons)
  {
    Assert.Throws<RoutePlanningException>(() => FuelOptimizer.Optimize(100, gallons, Profile(),
      [Station("First purchase below reserve", 15, 3)], 1, false, compare: false, arrivalPolicy: Arrival()));
  }

  [Fact]
  public void RecoveryBridgeRestoresReserveAndConnectsToTheCheaperMainFill()
  {
    var nearby = Station("Expensive nearby recovery", 5, 6);
    var cheaper = Station("Cheaper main fill", 50, 3);
    var selected = FuelRouteSearch.SelectCandidates([nearby, cheaper], 300, 2, Profile(), Arrival(), new());
    var chains = FuelRouteSearch.Chains(selected, 300, 2, Profile(), Arrival());
    var plan = FuelOptimizer.Optimize(300, 2, Profile(), chains[0], 1, false,
      compare: false, arrivalPolicy: Arrival());

    Assert.Equal(new[] { nearby.Station.StationId, cheaper.Station.StationId }, plan.Stops.Select(x => x.StationId));
    Assert.Equal(1, plan.Stops[0].ArrivalGallons);
    Assert.Equal(30, plan.Stops[0].BuyGallons);
    Assert.Equal(31, plan.Stops[0].DepartureGallons);
    Assert.Equal(22, plan.Stops[1].ArrivalGallons);
    Assert.Equal(40, plan.Stops[1].BuyGallons);
    Assert.Equal(12, plan.ArrivalGallons);
    Assert.Equal(340, plan.EconomicCostUsd);
  }

  [Fact]
  public void LowFuelBackbonePreservesTheFirstRecoveryAndTheFullItinerary()
  {
    var recovery = Station("Initial recovery", 5, 6);
    var candidates = Enumerable.Range(1, 19).Select(i => Station($"Later {i}", i * 400, 4)).Prepend(recovery).ToList();
    var selected = FuelRouteSearch.SelectCandidates(candidates, 8000, 2, Profile(), Arrival(), new());
    var plan = FuelOptimizer.Optimize(8000, 2, Profile(), selected, 1, false,
      compare: false, arrivalPolicy: Arrival());

    Assert.Equal(recovery.Station.StationId, plan.Stops[0].StationId);
    Assert.Equal(1, plan.Stops[0].ArrivalGallons);
    Assert.All(plan.Stops.Skip(1), stop => Assert.True(stop.ArrivalGallons >= 10));
    Assert.True(plan.ArrivalGallons >= 10);
    Assert.InRange(selected.Count, 20, 24);
  }
}
