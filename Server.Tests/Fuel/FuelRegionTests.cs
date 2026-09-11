using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Options;
using Application.Features.Routing.Models;
using Application.Features.Fuel.Queries.GetFuelStations;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
public class FuelRegionTests
{
  [Fact]
  public void HalfTankArrivalFillsAtCheapEarlyStationAndBuysOnlyNeededTopUpLater()
  {
    var later = new FuelCandidate(new() { StationId = Guid.NewGuid(), Name = "Nevada" }, 450, 0, 0, 4, 4);
    var policy = Policy(50, 50, 6);
    policy.PoorArea = true;
    policy.EconomicPurchasesOnly = true;
    var plan = FuelOptimizer.Optimize(600, 40, Profile(), [Station(3), later], 1, false, arrivalPolicy: policy);
    Assert.Equal(2, plan.Stops.Count);
    Assert.Equal(100, plan.Stops[0].DepartureGallons);
    Assert.Equal(60, plan.Stops[1].BuyGallons);
    Assert.False(plan.Stops[1].FillToTarget);
    Assert.Equal(50, plan.ArrivalGallons);
  }

  [Fact]
  public void HalfTankFloorDoesNotForceAnUnneededTopUp()
  {
    var policy = Policy(50, 50, 6);
    policy.PoorArea = true;
    policy.EconomicPurchasesOnly = true;
    var plan = FuelOptimizer.Optimize(100, 100, Profile(), [Station(3)], 1, false, arrivalPolicy: policy);
    Assert.Empty(plan.Stops);
    Assert.Equal(80, plan.ArrivalGallons);
  }
  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 100, Mpg = 5,
    ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20 };
  private static FuelCandidate Station(double price) => new(new() { StationId = Guid.NewGuid(), Name = "En route", YourPrice = price }, 50, 0, 0, price, price);
  private static FuelArrivalPolicy Policy(double minimum, double target, double price) => new()
    { MinimumGallons = minimum, TargetGallons = target, ReplacementPriceUsd = price, Reason = "After delivery reserve" };

  [Fact]
  public void RefillsEvenWhenDeliveryIsReachableToKeepFuelForExit()
  {
    var p = Profile();
    var old = FuelOptimizer.Optimize(100, 40, p, [Station(3)], 1, false);
    var regional = FuelOptimizer.Optimize(100, 40, p, [Station(3)], 1, false, arrivalPolicy: Policy(40, 40, 5));
    Assert.Empty(old.Stops);
    Assert.Equal(25, Assert.Single(regional.Stops).BuyGallons);
    Assert.Equal(45, regional.ArrivalGallons);
  }

  [Fact]
  public void BuysExtraCheapFuelBeforeExpensiveAreaBeyondMandatoryReserve()
  {
    var plan = FuelOptimizer.Optimize(100, 50, Profile(), [Station(3)], 1, false, arrivalPolicy: Policy(20, 60, 5));
    Assert.Equal(30, Assert.Single(plan.Stops).BuyGallons);
    Assert.Equal(60, plan.ArrivalGallons);
  }

  [Fact]
  public void DoesNotAddStopForPenniesOfFutureSavings()
  {
    var plan = FuelOptimizer.Optimize(100, 50, Profile(), [Station(4.999)], 1, false, arrivalPolicy: Policy(20, 60, 5));
    Assert.Empty(plan.Stops);
    Assert.Equal(30, plan.ArrivalGallons);
  }

  [Fact]
  public void ArrivalReserveIsNeverSilentlyClippedToCapacity()
  {
    Assert.Throws<RoutePlanningException>(() => FuelOptimizer.Optimize(100, 50, Profile(), [Station(3)], 1, false,
      arrivalPolicy: Policy(110, 110, 5)));
  }

  [Fact]
  public void FullTankDoesNotForceAnImmediatePurchase()
  {
    var plan = FuelOptimizer.Optimize(100, 100, Profile(), [Station(3)], 1, false, arrivalPolicy: Policy(30, 60, 5));
    Assert.Empty(plan.Stops);
    Assert.Equal(80, plan.ArrivalGallons);
  }

  [Theory]
  [InlineData(90)]
  [InlineData(100)]
  public void UnknownOnwardRouteInPoorAreaFillsToConfiguredMaximum(int fillPercent)
  {
    var profile = Profile(); profile.FillPercent = fillPercent;
    var policy = Policy(20, 30, 3); policy.PoorArea = true;
    var plan = FuelOptimizer.Optimize(100, 50, profile, [Station(3)], 1, false, arrivalPolicy: policy);
    var stop = Assert.Single(plan.Stops);
    Assert.Equal(fillPercent, stop.DepartureGallons);
    Assert.True(stop.FillToTarget);
    Assert.Equal(fillPercent - 10, plan.ArrivalGallons);
  }

  [Fact]
  public void KnownNextDispatchDoesNotForceMaximumFill()
  {
    var policy = Policy(20, 30, 3); policy.PoorArea = true; policy.NextDispatchId = Guid.NewGuid();
    var plan = FuelOptimizer.Optimize(100, 50, Profile(), [Station(3)], 1, false, arrivalPolicy: policy);
    Assert.Empty(plan.Stops);
  }

  [Fact]
  public void UnknownPoorAreaDoesNotForceAnotherStopWhenAlreadyFull()
  {
    var policy = Policy(20, 30, 3); policy.PoorArea = true;
    var plan = FuelOptimizer.Optimize(100, 100, Profile(), [Station(3)], 1, false, arrivalPolicy: policy);
    Assert.Empty(plan.Stops);
  }

  [Fact]
  public void UnknownPoorAreaWithoutAReachableStationKeepsFeasibleReservePlan()
  {
    var policy = Policy(20, 30, 3); policy.PoorArea = true;
    var plan = FuelOptimizer.Optimize(100, 50, Profile(), [], 1, false, arrivalPolicy: policy);
    Assert.Empty(plan.Stops);
    Assert.Equal(30, plan.ArrivalGallons);
  }

  [Theory]
  [InlineData(150, false)]
  [InlineData(145, false)]
  [InlineData(155, false)]
  [InlineData(175, true)]
  [InlineData(180, true)]
  public void TopsUpBeforeExpensiveDestinationOnlyWithEnoughHeadroomForMinimumPurchase(double laterMiles, bool topUp)
  {
    var first = Station(3);
    var later = new FuelCandidate(new() { StationId = Guid.NewGuid(), Name = "Before California", YourPrice = 4 },
      laterMiles, 0, 0, 4, 4);
    var policy = Policy(20, 20, 6); policy.PoorArea = true; policy.TopUpPriceCeilingUsd = 5.85;
    var plan = FuelOptimizer.Optimize(400, 30, Profile(), [first, later], 1, false, arrivalPolicy: policy);
    Assert.Equal(100, plan.Stops[0].DepartureGallons);
    Assert.Equal(topUp ? 2 : 1, plan.Stops.Count);
    if (topUp)
    {
      Assert.Equal(later.Station.StationId, plan.Stops[1].StationId);
      Assert.Equal(100, plan.Stops[1].DepartureGallons);
      Assert.True(plan.Stops[1].ArrivalGallons <= 80);
      Assert.Equal(100 - plan.Stops[1].ArrivalGallons, plan.Stops[1].BuyGallons);
    }
  }

  [Fact]
  public void DestinationTopUpAlsoAppliesWithAssignedNextDispatchAndRespectsFillLimit()
  {
    var profile = Profile(); profile.FillPercent = 90;
    var later = new FuelCandidate(new() { StationId = Guid.NewGuid(), YourPrice = 4 }, 175, 0, 0, 4, 4);
    var policy = Policy(20, 20, 6); policy.PoorArea = true;
    policy.NextDispatchId = Guid.NewGuid(); policy.TopUpPriceCeilingUsd = 5.85;
    var plan = FuelOptimizer.Optimize(400, 30, profile, [Station(3), later], 1, false, arrivalPolicy: policy);
    Assert.Equal(2, plan.Stops.Count);
    Assert.Equal(90, plan.Stops[1].DepartureGallons);
    Assert.Equal(25, plan.Stops[1].BuyGallons);
  }

  [Fact]
  public void DoesNotTopUpForStationAsExpensiveAsDestination()
  {
    var later = new FuelCandidate(new() { StationId = Guid.NewGuid(), YourPrice = 6 }, 150, 0, 0, 6, 6);
    var policy = Policy(20, 20, 6); policy.PoorArea = true; policy.TopUpPriceCeilingUsd = 5.85;
    var plan = FuelOptimizer.Optimize(400, 30, Profile(), [Station(3), later], 1, false, arrivalPolicy: policy);
    Assert.Single(plan.Stops);
  }

  [Fact]
  public void OneGallonShortfallProducesUsefulPurchaseWithoutDroppingReserve()
  {
    var plan = FuelOptimizer.Optimize(100, 29, Profile(), [Station(3)], 1, false);
    Assert.Equal(25, Assert.Single(plan.Stops).BuyGallons);
    Assert.True(plan.ArrivalGallons >= 10);
  }

  [Fact]
  public void HighFuelDoesNotAddTokenPurchaseNearDelivery()
  {
    var profile = Profile(); profile.FillPercent = 90;
    var policy = Policy(20, 95, 6); policy.PoorArea = true;
    var plan = FuelOptimizer.Optimize(50, 100, profile, [new(new() { StationId = Guid.NewGuid() }, 40, 0, 0, 3, 3)],
      1, false, arrivalPolicy: policy);
    Assert.Empty(plan.Stops);
    Assert.Equal(90, plan.ArrivalGallons);
  }

  [Fact]
  public void PoorAreaCannotBeSatisfiedByTinyPurchaseInsteadOfFullRefill()
  {
    var policy = Policy(20, 30, 6); policy.PoorArea = true; policy.TopUpPriceCeilingUsd = 5.85;
    var nearDelivery = new FuelCandidate(new() { StationId = Guid.NewGuid() }, 390, 0, 0, 5, 5);
    var plan = FuelOptimizer.Optimize(400, 30, Profile(), [Station(3), nearDelivery], 1, false, arrivalPolicy: policy);
    Assert.All(plan.Stops, stop => Assert.True(stop.BuyGallons >= 10));
    Assert.True(plan.Stops[^1].FillToTarget);
  }

  [Fact]
  public void GridSeparatesExpensiveSparseAndUnknownData()
  {
    var point = new RoutePoint(40.1, -100.1);
    PricedFuelStation Priced(double price) => new(new() { StationId = Guid.NewGuid(), Point = point }, price, price);
    var options = new FuelRegionOptions();
    Assert.Equal("unknown", new FuelRegionGrid([], options, 3).Cell(point).Kind);
    Assert.Equal("sparse", new FuelRegionGrid([Priced(4)], options, 3).Cell(point).Kind);
    var cell = new FuelRegionGrid([Priced(4), Priced(4.1)], options, 3).Cell(point);
    Assert.Equal("expensive", cell.Kind);
    Assert.InRange(point.Latitude, cell.South, cell.North);
    Assert.InRange(point.Longitude, cell.West, cell.East);
    Assert.Equal("good", new FuelRegionGrid([Priced(3), Priced(3.1)], options, 3).Cell(point).Kind);
  }

  [Fact]
  public void PricesRespectDateCurrencyUnitsAndIfta()
  {
    var date = new DateOnly(2026, 9, 5);
    var ca = new FuelStationDto(Guid.NewGuid(), "1", "CA", "", "", "ON", "", "CA", 40, -80,
      [new("CAD", "Diesel", 2, 1.5m, .5m, date, date, 1.3m, "L")]);
    var p = Profile(); p.UseIfta = true;
    Assert.Empty(FuelRegionGrid.Prices([ca], p, date));
    p.CadToUsd = .75;
    var normalized = Assert.Single(FuelRegionGrid.Prices([ca], p, date));
    Assert.Equal(1.3 * 3.785411784 * .75, normalized.EconomicUsd, 6);
    Assert.Empty(FuelRegionGrid.Prices([ca], p, date.AddDays(1)));
  }
}
