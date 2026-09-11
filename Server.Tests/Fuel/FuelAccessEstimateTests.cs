using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelAccessEstimateTests
{
  [Theory]
  [InlineData(0, 0, 0)]
  [InlineData(.1, .5, 4)]
  [InlineData(1, 1.5, 8)]
  [InlineData(2, 3, 14)]
  [InlineData(6.67, 10.005, 42.02)]
  [InlineData(10, 15, 62)]
  [InlineData(15, 22.5, 92)]
  [InlineData(20, 30, 122)]
  [InlineData(40, 60, 242)]
  public void NearbyStationsRetainPriceAndVisitWithRoundTripFuelAndSlowAccessTime(double away, double access, double minutes)
  {
    var source = Station("Fuel", 40, 3, away, 2);
    var result = Assert.Single(FuelAccessEstimate.Nearby([source]));
    Assert.Equal(source.VisitKey, result.VisitKey);
    Assert.Equal(source.EconomicPriceUsd, result.EconomicPriceUsd);
    Assert.Equal(source.PriceUsd, result.PriceUsd);
    Assert.Equal(access, result.ExtraInMiles, 6);
    Assert.Equal(access, result.ExtraOutMiles, 6);
    Assert.Equal(minutes, result.Station.DetourMinutes, 6);
    Assert.NotSame(source.Station, result.Station);
    Assert.Equal(0, source.Station.DetourMinutes);
  }

  [Theory]
  [InlineData(40.0001)]
  [InlineData(-1)]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  [InlineData(double.NegativeInfinity)]
  public void DistantOrInvalidAccessCannotBecomeAnUnverifiedShortcut(double away) =>
    Assert.Empty(FuelAccessEstimate.Nearby([Station("Fuel", 40, 1, away)]));

  [Fact]
  public void StationSearchRadiusDoesNotWidenCurrentGpsMatching()
  {
    Assert.Equal(40, FuelAccessEstimate.NearbyMiles);
    Assert.Equal(40, FuelAccessEstimate.CurrentPositionToleranceMiles);
  }

  [Theory]
  [InlineData(1, 103, 6480)]
  [InlineData(6.67, 120.01, 8521.2)]
  public void TimingEstimateSharesSavedCoordinatesAndNeverChangesBaselineMileage(double away, double miles, double seconds)
  {
    List<RoutePoint> points = [new(40, -80), new(40, -79)];
    var baseline = new TruckRoute { Miles = 100, Seconds = 6000, Legs = [new(100, 6000, points)] };
    var candidate = Assert.Single(FuelAccessEstimate.Nearby([Station("Fuel", 40, 3, away)]));
    var result = FuelAccessEstimate.TimingRoute(baseline, [candidate]);
    Assert.Equal(miles, result.Miles, 6);
    Assert.Equal(seconds, result.Seconds, 6);
    Assert.Same(points, result.Legs[0].Points);
    Assert.Equal(100, baseline.Miles);
    Assert.Equal(6000, baseline.Seconds);
    Assert.Equal(100, baseline.Legs[0].Miles);
    Assert.Empty(result.Points);
  }

  [Fact]
  public void WiderStationAccessPaysFuelBothWaysAndTheExistingStopAndTimeCosts()
  {
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      FillPercent = 100, ReserveGallons = 10, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
    var arrival = new FuelArrivalPolicy { MinimumGallons = 10, TargetGallons = 10,
      ReplacementPriceUsd = 4, EconomicPurchasesOnly = true };
    var candidates = FuelAccessEstimate.Nearby([Station("6.67 miles away", 100, 2, 6.67)]);
    var plan = FuelOptimizer.Optimize(300, 50, profile, candidates, 1, false, compare: false, arrivalPolicy: arrival);
    var purchase = Assert.Single(plan.Stops);

    Assert.Equal(27.999, purchase.ArrivalGallons, 6);
    Assert.Equal(25, purchase.BuyGallons);
    Assert.Equal(52.999, purchase.DepartureGallons, 6);
    Assert.Equal(10.998, plan.ArrivalGallons, 6);
    Assert.Equal(20.01, purchase.DetourMiles, 6);
    Assert.Equal(42.02, purchase.DetourMinutes, 6);
    Assert.Equal(50, plan.PurchaseCostUsd);
    Assert.Equal(50 + 20 + 42.02 / 60 * 35, plan.EconomicCostUsd, 6);
  }

  [Theory]
  [InlineData(6.67, 2, 2.1, "Nearby")]
  [InlineData(6.67, .25, 2.1, "Distant")]
  [InlineData(20, 4.9, 5, "Nearby")]
  [InlineData(20, .25, 5, "Distant")]
  [InlineData(40, 4.9, 5, "Nearby")]
  [InlineData(40, .25, 5, "Nearby")]
  [InlineData(40, .25, 10, "Distant")]
  public void WiderEligibleStationWinsOnlyWhenItsSavingsPayForAccess(double away, double distantPrice, double nearbyPrice, string winner)
  {
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      FillPercent = 100, ReserveGallons = 10, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
    var arrival = new FuelArrivalPolicy { MinimumGallons = 10, TargetGallons = 10,
      ReplacementPriceUsd = 4, EconomicPurchasesOnly = true };
    var candidates = FuelAccessEstimate.Nearby([
      Station("Distant", 100, distantPrice, away), Station("Nearby", 120, nearbyPrice)]);
    Assert.Equal(2, candidates.Count);
    var plan = FuelOptimizer.Optimize(300, 50, profile, candidates, 1, false, compare: false, arrivalPolicy: arrival);

    Assert.Equal(winner, Assert.Single(plan.Stops).Name);
    Assert.True(plan.ArrivalGallons >= profile.ReserveGallons);
    Assert.Equal(plan.PurchaseCostUsd + 20 + plan.ExtraMinutes / 60 * 35, plan.EconomicCostUsd, 6);
  }

  [Fact]
  public void AccessAwareTierRestoresAnEarlierBridgeWhenCheapStationIsNotReachableWithItsAccess()
  {
    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      FillPercent = 100, ReserveGallons = 10, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
    var arrival = new FuelArrivalPolicy { MinimumGallons = 10, TargetGallons = 10, ReplacementPriceUsd = 5,
      EconomicPurchasesOnly = true };
    var candidates = FuelAccessEstimate.Nearby([Station("Cheap", 100, 2, 2), Station("Bridge", 50, 5)]);
    var chains = FuelRouteSearch.Chains(candidates, 400, 30, profile, arrival, includeAccess: true);
    var winner = FuelOptimizer.Optimize(400, 30, profile, chains[0], 1, false, compare: false, arrivalPolicy: arrival);
    Assert.Equal(new[] { "Bridge", "Cheap" }, winner.Stops.Select(x => x.Name));
    Assert.InRange(winner.Stops[0].BuyGallons, 10, 30);
    Assert.False(winner.Stops[0].FillToTarget);
    Assert.All(winner.Stops, x => Assert.True(x.ArrivalGallons >= 10));
    Assert.True(winner.ArrivalGallons >= 10);
    Assert.Equal(winner.PurchaseCostUsd + 40 + 14d / 60 * 35, winner.EconomicCostUsd, 6);
  }

  private static FuelCandidate Station(string name, double mile, double price, double away = 0, int leg = 0) =>
    new(new() { StationId = Guid.NewGuid(), Name = name, Point = new(40, -80), YourPrice = price,
      EconomicPrice = price, Currency = "USD", Unit = "US gal" }, mile, away, 0, price, price) { LegIndex = leg };
}
