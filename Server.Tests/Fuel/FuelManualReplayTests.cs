using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelManualReplayTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PurchaseLimitUsesArrivalHeadroomForPartialAndFullPurchases(bool full)
  {
    var profile = Profile();
    profile.TankGallons = 211.3;
    var station = Station(10);
    var result = FuelManualReplay.Evaluate(20, 36, profile, [station], [Edit(station, full ? 0 : 170, full)], Arrival(), 1);

    Assert.Empty(result.Errors);
    Assert.Equal(34, Assert.Single(result.Plan.Stops).ArrivalGallons, 10);
    Assert.Equal(177.3, Assert.Single(result.PurchaseLimitsGallons)!.Value, 10);
    Assert.Equal(full ? 177.3 : 170, result.Plan.Stops[0].BuyGallons, 10);
  }

  [Fact]
  public void CurrentOverfillHasACorrectiveLimitButCannotEstablishLaterHeadroom()
  {
    var first = Station(10);
    var second = Station(20);
    var result = Replay(100, 100, [first, second], [Edit(first, 120), Edit(second, 30)]);

    Assert.Contains(result.Errors, error => error.Contains("fill limit"));
    Assert.Equal(102, result.PurchaseLimitsGallons[0]);
    Assert.Null(result.PurchaseLimitsGallons[1]);
  }

  [Fact]
  public void HeadroomHonorsConfiguredFillPercentWithoutRoundingToSliderSteps()
  {
    var profile = Profile();
    profile.TankGallons = 211.3;
    profile.FillPercent = 90;
    var station = Station(10);
    var result = FuelManualReplay.Evaluate(20, 36, profile, [station], [Edit(station, 0, true)], Arrival(), 1);

    Assert.Empty(result.Errors);
    Assert.Equal(156.17, Assert.Single(result.PurchaseLimitsGallons)!.Value, 10);
  }

  [Fact]
  public void AddingAndRemovingAVisitReplaysEveryLaterQuantityWithoutSelectingOtherStations()
  {
    var first = Station(50);
    var second = Station(150);
    var third = Station(250);
    var original = Replay(300, 50, [first, second], [Edit(first, 30), Edit(second, 40)]);
    var added = Replay(300, 50, [first, second, third], [Edit(first, 30), Edit(second, 40), Edit(third, 20)]);
    var removed = Replay(300, 50, [second], [Edit(second, 40)]);

    Assert.Empty(original.Errors);
    Assert.Empty(added.Errors);
    Assert.Empty(removed.Errors);
    Assert.Equal(50, original.Plan.Stops[1].ArrivalGallons);
    Assert.Equal(60, original.Plan.ArrivalGallons);
    Assert.Equal(80, added.Plan.ArrivalGallons);
    Assert.Equal(20, Assert.Single(removed.Plan.Stops).ArrivalGallons);
    Assert.Equal(30, removed.Plan.ArrivalGallons);
    Assert.Equal(new[] { 1, 2, 3 }, added.Plan.Stops.Select(x => x.Number));
    Assert.Equal(1, removed.Plan.Stops[0].Number);
    Assert.False(added.Plan.NeedsRefresh);
  }

  [Fact]
  public void DeletingARequiredStopKeepsTheUnsafeDraftAndReportsItsFinalShortage()
  {
    var station = Station(50);
    Assert.Empty(Replay(300, 30, [station], [Edit(station, 80)]).Errors);
    var removed = Replay(300, 30, [], []);

    Assert.Empty(removed.Plan.Stops);
    Assert.Equal(-30, removed.Plan.ArrivalGallons);
    Assert.Contains(removed.Errors, x => x.Contains("final arrival", StringComparison.OrdinalIgnoreCase));
    Assert.True(removed.Plan.NeedsRefresh);
    Assert.Equal(removed.Errors, removed.Plan.RefreshReasons);
  }

  [Theory]
  [InlineData(100)]
  [InlineData(90)]
  public void FullTargetIncludesTheExactFractionAndDoesNotBuyItTwice(double fillPercent)
  {
    var profile = Profile();
    profile.TankGallons = 211.33764188651872;
    profile.FillPercent = fillPercent;
    var cap = profile.TankGallons.Value * fillPercent / 100;
    var first = Station(10, 3, 2.5);
    var second = Station(910, 4, 3.5);
    var result = FuelManualReplay.Evaluate(1000, 36.75, profile, [first, second],
      [Edit(first, 0, true), Edit(second, 0, true)], Arrival(cap), 7);

    Assert.Empty(result.Errors);
    Assert.Equal(34.75, result.Plan.Stops[0].ArrivalGallons, 10);
    Assert.Equal(cap - 34.75, result.Plan.Stops[0].BuyGallons, 10);
    Assert.Equal(cap - 180, result.Plan.Stops[1].ArrivalGallons, 10);
    Assert.Equal(180, result.Plan.Stops[1].BuyGallons, 10);
    Assert.All(result.Plan.Stops, stop =>
    {
      Assert.True(stop.FillToTarget);
      Assert.Equal(cap, stop.DepartureGallons, 10);
      Assert.Equal(stop.DepartureGallons, stop.ArrivalGallons + stop.BuyGallons, 10);
    });
    Assert.Equal(cap - 18, result.Plan.ArrivalGallons, 10);
    Assert.Equal((cap - 34.75) * 3 + 180 * 4, result.Plan.PurchaseCostUsd, 8);
    Assert.Equal((cap - 34.75) * 2.5 + 180 * 3.5 + 40, result.Plan.EconomicCostUsd, 8);
    Assert.Equal(18 * 4, result.Plan.ExpectedFutureFuelCostUsd, 8);
  }

  [Fact]
  public void AnExactPartialPurchaseIsNotRoundedToSliderStepsOrSilentlyConvertedToFull()
  {
    var station = Station(7.5);
    var result = Replay(20, 40.75, [station], [Edit(station, 37.4)]);

    Assert.Empty(result.Errors);
    var stop = Assert.Single(result.Plan.Stops);
    Assert.Equal(39.25, stop.ArrivalGallons, 10);
    Assert.Equal(37.4, stop.BuyGallons);
    Assert.Equal(76.65, stop.DepartureGallons, 10);
    Assert.False(stop.FillToTarget);
    Assert.Equal(74.15, result.Plan.ArrivalGallons, 10);
  }

  [Fact]
  public void ReplayingAnOverflowDoesNotClampOrSilentlySaveTheRequestedQuantity()
  {
    var station = Station(10);
    var result = Replay(100, 100, [station], [Edit(station, 120)]);

    var stop = Assert.Single(result.Plan.Stops);
    Assert.Equal(120, stop.BuyGallons);
    Assert.Equal(218, stop.DepartureGallons);
    Assert.Contains(result.Errors, x => x.Contains("fill limit"));
    Assert.True(result.Plan.NeedsRefresh);
  }

  [Fact]
  public void AConfiguredLowerFillLimitStillApplies()
  {
    var profile = Profile();
    profile.FillPercent = 50;
    var station = Station(10);
    var result = FuelManualReplay.Evaluate(100, 60, profile, [station], [Edit(station, 50)], Arrival(), 1);

    Assert.Equal(108, result.Plan.Stops[0].DepartureGallons);
    Assert.Contains(result.Errors, x => x.Contains("fill limit"));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  [InlineData(10)]
  public void PositiveDepletedReserveMayReachTheFirstStopBeforeRestoringNormalReserve(double firstMiles)
  {
    var station = Station(firstMiles);
    var result = Replay(100, 2, [station], [Edit(station, 40)]);

    Assert.Empty(result.Errors);
    Assert.InRange(result.Plan.Stops[0].ArrivalGallons, 0, 2);
    Assert.True(result.Plan.Stops[0].DepartureGallons >= 10);
    Assert.Equal(22, result.Plan.ArrivalGallons, 10);
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(2, 10.01)]
  [InlineData(-1, 0)]
  [InlineData(201, 0)]
  public void EmptyUnreachableOrInvalidInitialFuelDoesNotProduceASafeDraft(double gallons, double firstMiles)
  {
    var station = Station(firstMiles);
    var result = Replay(100, gallons, [station], [Edit(station, 40)]);

    Assert.NotEmpty(result.Errors);
    Assert.True(result.Plan.NeedsRefresh);
    Assert.All(result.PurchaseLimitsGallons, Assert.Null);
  }

  [Fact]
  public void InitialAccessCannotTurnAStartingReserveIntoTheLowFuelException()
  {
    var station = Station(0);
    var result = FuelManualReplay.Evaluate(20, 10, Profile(), [station], [Edit(station, 30)], Arrival(), 1, 5);

    Assert.Equal(9, result.Plan.Stops[0].ArrivalGallons);
    Assert.Contains(result.Errors, x => x.Contains("required reserve"));
    Assert.Equal(191, Assert.Single(result.PurchaseLimitsGallons));
  }

  [Fact]
  public void LowFuelExceptionDoesNotApplyToLaterPurchases()
  {
    var first = Station(5);
    var second = Station(50);
    var result = Replay(100, 2, [first, second], [Edit(first, 10), Edit(second, 40)]);

    Assert.Equal(1, result.Plan.Stops[0].ArrivalGallons);
    Assert.Equal(11, result.Plan.Stops[0].DepartureGallons);
    Assert.Equal(2, result.Plan.Stops[1].ArrivalGallons);
    Assert.Contains(result.Errors, x => x.StartsWith("Fuel stop 2 cannot be reached"));
  }

  [Fact]
  public void FirstRecoveryPurchaseMustRestoreTheConfiguredReserve()
  {
    var profile = Profile();
    profile.ReserveGallons = 25;
    var station = Station(5);
    var result = FuelManualReplay.Evaluate(10, 2, profile, [station], [Edit(station, 10)], Arrival(), 1);

    Assert.Equal(1, result.Plan.Stops[0].ArrivalGallons);
    Assert.Equal(11, result.Plan.Stops[0].DepartureGallons);
    Assert.Contains(result.Errors, x => x.Contains("does not restore"));
  }

  [Fact]
  public void ChangingAnEarlierBuyRecalculatesTheLaterFullQuantityWithoutChangingItsTarget()
  {
    var first = Station(50);
    var full = Station(150);
    var before = Replay(300, 50, [first, full], [Edit(first, 30), Edit(full, 0, true)]);
    var after = Replay(300, 50, [first, full], [Edit(first, 40), Edit(full, 0, true)]);

    Assert.Empty(before.Errors);
    Assert.Empty(after.Errors);
    Assert.Equal(150, before.Plan.Stops[1].BuyGallons);
    Assert.Equal(140, after.Plan.Stops[1].BuyGallons);
    Assert.Equal(before.Plan.Stops[1].DepartureGallons, after.Plan.Stops[1].DepartureGallons);
    Assert.Equal(before.Plan.ArrivalGallons, after.Plan.ArrivalGallons);
    Assert.Equal(new double?[] { 160, 150 }, before.PurchaseLimitsGallons);
    Assert.Equal(new double?[] { 160, 140 }, after.PurchaseLimitsGallons);
  }

  [Fact]
  public void TerminalPolicyCannotWeakenNormalReserveAndCanRequireMoreFuel()
  {
    var weak = FuelManualReplay.Evaluate(100, 25, Profile(), [], [],
      new() { MinimumGallons = 1, TargetGallons = 1, ReplacementPriceUsd = 4 }, 1);
    var stronger = FuelManualReplay.Evaluate(100, 35, Profile(), [], [],
      new() { MinimumGallons = 20, TargetGallons = 30, ReplacementPriceUsd = 4 }, 1);

    Assert.Equal(5, weak.Plan.ArrivalGallons);
    Assert.Contains(weak.Errors, x => x.StartsWith("The final arrival"));
    Assert.Equal(15, stronger.Plan.ArrivalGallons);
    Assert.Contains(stronger.Errors, x => x.StartsWith("The final arrival"));
    Assert.Equal(60, stronger.Plan.ExpectedFutureFuelCostUsd);
  }

  [Fact]
  public void AccessFuelTimeStopChargesAndReplacementValueAreCountedExactlyOnce()
  {
    var first = Station(100, 4, 3.5) with { ExtraInMiles = 2, ExtraOutMiles = 3 };
    var second = Station(200, 3, 2.5) with { ExtraInMiles = 1, ExtraOutMiles = 4 };
    var result = FuelManualReplay.Evaluate(300, 60, Profile(), [first, second],
      [Edit(first, 30), Edit(second, 40)], Arrival(100), 8, 5);

    Assert.Empty(result.Errors);
    Assert.Equal(38.6, result.Plan.Stops[0].ArrivalGallons, 10);
    Assert.Equal(47.8, result.Plan.Stops[1].ArrivalGallons, 10);
    Assert.Equal(67, result.Plan.ArrivalGallons, 10);
    Assert.Equal(315, result.Plan.RemainingMiles);
    Assert.Equal(100, result.Plan.Stops[0].RouteMilesAhead);
    Assert.Equal(107, result.Plan.Stops[0].MilesAhead);
    Assert.Equal(200, result.Plan.Stops[1].RouteMilesAhead);
    Assert.Equal(211, result.Plan.Stops[1].MilesAhead);
    Assert.Equal(36, result.Plan.ExtraMinutes);
    Assert.Equal(240, result.Plan.PurchaseCostUsd);
    Assert.Equal(205 + 40 + 36d / 60 * 35, result.Plan.EconomicCostUsd, 10);
    Assert.Equal(132, result.Plan.ExpectedFutureFuelCostUsd, 10);
    Assert.Equal(8, result.Plan.RouteVersion);
    Assert.Equal(FuelOptimizer.SelectionVersion, result.Plan.SelectionVersion);
    Assert.Null(result.Plan.SavingsUsd);
  }

  [Fact]
  public void DirectTravelConsumesInitialAccessOnceAndPricesItsTime()
  {
    var result = FuelManualReplay.Evaluate(50, 30, Profile(), [], [], Arrival(), 1, 10);

    Assert.Empty(result.Errors);
    Assert.Equal(18, result.Plan.ArrivalGallons);
    Assert.Equal(60, result.Plan.RemainingMiles);
    Assert.Equal(22, result.Plan.ExtraMinutes);
    Assert.Equal(22d / 60 * 35, result.Plan.EconomicCostUsd, 10);
    Assert.Equal(0, result.Plan.PurchaseCostUsd);
  }

  [Fact]
  public void ClonedStationRetainsResolvedOwnershipAndNormalizedPricesWithoutMutatingInput()
  {
    var station = Station(10, 4, 3);
    station.Station.Number = 99;
    station.Station.BuyGallons = 999;
    station.Station.DetourMinutes = 999;
    var result = Replay(100, 30, [station], [Edit(station, 30)]);

    Assert.Empty(result.Errors);
    var stop = Assert.Single(result.Plan.Stops);
    Assert.NotSame(station.Station, stop);
    Assert.Equal(station.Station.DispatchId, stop.DispatchId);
    Assert.Equal(station.Station.BeforeStopId, stop.BeforeStopId);
    Assert.Equal(station.VisitKey, stop.VisitKey);
    Assert.Equal(4, stop.CashUsdPerGallon);
    Assert.Equal(3, stop.EconomicUsdPerGallon);
    Assert.Equal(99, station.Station.Number);
    Assert.Equal(999, station.Station.BuyGallons);
    Assert.Equal(999, station.Station.DetourMinutes);
    Assert.Equal(0, stop.DetourMinutes);
    Assert.Null(stop.CurrentRouteMile);
  }

  [Fact]
  public void DuplicateOccurrenceAndOutOfOrderVisitsAreRejectedButASeparateReturnVisitIsAllowed()
  {
    var first = Station(50);
    var second = Station(150);
    var duplicate = Replay(300, 50, [first, first], [Edit(first, 30), Edit(first, 30)]);
    var reversed = Replay(300, 50, [second, first], [Edit(second, 30), Edit(first, 30)]);
    var returned = first with { AlongMiles = 250, LegIndex = 1 };
    var valid = Replay(300, 50, [first, returned], [Edit(first, 30), Edit(returned, 30)]);

    Assert.Contains(duplicate.Errors, x => x.Contains("unique station visit"));
    Assert.Contains(reversed.Errors, x => x.Contains("out of route order"));
    Assert.Empty(valid.Errors);
    Assert.Equal(2, valid.Plan.Stops.Count);
    Assert.NotEqual(valid.Plan.Stops[0].VisitKey, valid.Plan.Stops[1].VisitKey);
  }

  [Fact]
  public void WrongOwnershipAndMismatchedRequestCountAreRejected()
  {
    var station = Station(10);
    var ownership = Replay(100, 50, [station], [new(station.Station.StationId, Guid.NewGuid(), 30, false)]);
    var identity = Replay(100, 50, [station], [new(Guid.NewGuid(), null, 30, false)]);
    var count = Replay(100, 50, [station], []);

    Assert.NotEmpty(ownership.Errors);
    Assert.NotEmpty(identity.Errors);
    Assert.NotEmpty(count.Errors);
    Assert.All(new[] { ownership, identity, count }, result => Assert.True(result.Plan.NeedsRefresh));
  }

  [Fact]
  public void OverlappingAccessAndInvalidProfileAreRejectedBeforeReplay()
  {
    var first = Station(50) with { EntryMiles = 45, ExitMiles = 70 };
    var second = Station(65) with { EntryMiles = 60, ExitMiles = 75 };
    var overlap = Replay(100, 50, [first, second], [Edit(first, 10), Edit(second, 10)]);
    var profile = Profile();
    profile.Mpg = 0;
    var invalid = FuelManualReplay.Evaluate(100, 50, profile, [], [], Arrival(), 1);

    Assert.Contains(overlap.Errors, x => x.Contains("out of route order"));
    Assert.NotEmpty(invalid.Errors);
    Assert.Empty(invalid.Plan.Stops);
  }

  [Fact]
  public void BoundedReplayRejectsAnOversizedDraftBeforeCalculatingIt()
  {
    var visits = Enumerable.Range(0, FuelManualReplay.MaximumStops + 1).Select(i => Station(i)).ToArray();
    var result = Replay(100, 50, visits, visits.Select(x => Edit(x, 10)).ToArray());

    Assert.NotEmpty(result.Errors);
    Assert.Empty(result.Plan.Stops);
  }

  [Theory]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  [InlineData(double.NegativeInfinity)]
  public void NonfiniteQuantitiesPricesDistancesAndFuelAreRejected(double invalid)
  {
    var station = Station(10);
    var results = new[]
    {
      Replay(100, 50, [station], [Edit(station, invalid)]),
      Replay(100, invalid, [station], [Edit(station, 10)]),
      Replay(invalid, 50, [station], [Edit(station, 10)]),
      Replay(100, 50, [station with { AlongMiles = invalid }], [Edit(station, 10)]),
      Replay(100, 50, [station with { ExtraInMiles = invalid }], [Edit(station, 10)]),
      Replay(100, 50, [station with { ExtraOutMiles = invalid }], [Edit(station, 10)]),
      Replay(100, 50, [station with { PriceUsd = invalid }], [Edit(station, 10)]),
      Replay(100, 50, [station with { EconomicPriceUsd = invalid }], [Edit(station, 10)]),
      FuelManualReplay.Evaluate(100, 50, Profile(), [station], [Edit(station, 10)], Arrival(), 1, invalid),
      FuelManualReplay.Evaluate(100, 50, Profile(), [station], [Edit(station, 10)],
        new() { MinimumGallons = invalid, TargetGallons = 20, ReplacementPriceUsd = 4 }, 1)
    };

    Assert.All(results, result =>
    {
      Assert.NotEmpty(result.Errors);
      Assert.True(result.Plan.NeedsRefresh);
      Assert.True(double.IsFinite(result.Plan.StartingGallons));
      Assert.True(double.IsFinite(result.Plan.ArrivalGallons));
      Assert.True(double.IsFinite(result.Plan.PurchaseCostUsd));
      Assert.True(double.IsFinite(result.Plan.RemainingMiles));
      Assert.All(result.PurchaseLimitsGallons, Assert.Null);
    });
  }

  [Fact]
  public void TinyPurchasesAreReportedWithoutBeingDroppedFromTheDraft()
  {
    var station = Station(10);
    var result = Replay(100, 30, [station], [Edit(station, 5)]);

    Assert.Equal(5, Assert.Single(result.Plan.Stops).BuyGallons);
    Assert.Contains(result.Errors, x => x.Contains("at least 10"));
  }

  private static FuelManualReplayResult Replay(double miles, double gallons,
    IReadOnlyList<FuelCandidate> visits, IReadOnlyList<FuelPlanEditStop> edits) =>
    FuelManualReplay.Evaluate(miles, gallons, Profile(), visits, edits, Arrival(), 1);

  private static FuelPlanEditStop Edit(FuelCandidate station, double gallons, bool full = false) =>
    new(station.Station.StationId, station.Station.BeforeStopId, gallons, full);

  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 200, Mpg = 5,
    ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20, DriverHourlyCostUsd = 35 };

  private static FuelArrivalPolicy Arrival(double target = 10) => new() { MinimumGallons = 10,
    TargetGallons = target, ReplacementPriceUsd = 4, EconomicPurchasesOnly = true };

  private static FuelCandidate Station(double miles, double cash = 4, double economic = 4) =>
    new(new() { StationId = Guid.NewGuid(), DispatchId = Guid.NewGuid(), BeforeStopId = Guid.NewGuid(),
      Name = "Station", Address = "Road", Point = new(40, -80), YourPrice = cash, EconomicPrice = economic,
      Currency = "USD", Unit = "US gal", PriceDate = new(2026, 9, 9) }, miles, 0, 0, cash, economic) { LegIndex = 0 };
}
