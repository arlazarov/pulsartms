using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPlanProjectionTests
{
  [Theory]
  [InlineData("pickup")]
  [InlineData("delivery")]
  [InlineData("departure")]
  public void CompletedFutureStopInvalidatesThePreviouslySavedItinerary(string completed)
  {
    var fixture = new Fixture();
    var stop = fixture.Loads[1].Stops[0];
    if (completed == "pickup") stop.PickedUpAt = fixture.Now;
    if (completed == "delivery") stop.DeliveredAt = fixture.Now;
    if (completed == "departure") stop.DepartedAt = fixture.Now;

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("Assigned stops", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PassedCurrentPrefixDoesNotInvalidateTheRemainingStopIdentity(bool confirmedCompleted)
  {
    var fixture = new Fixture();
    var load = fixture.Loads[0];
    var first = load.Stops[0];
    var second = fixture.Loads[1].Stops[0];
    load.Stops.Add(second);
    second.Sequence = 2;
    var itinerary = fixture.Saved.Stops.Select((x, i) => i == 1 ? x with { DispatchId = load.Id } : x).ToList();
    if (confirmedCompleted) first.DeliveredAt = fixture.Now;

    Assert.True(FuelPlanProjection.RemainingStopsMatch(itinerary, load.Id, second.Id,
      [load, fixture.Loads[2]]));
  }

  [Fact]
  public void CompletedPrefixCanDisappearWhenTheTruckAdvancesToTheNextAssignment()
  {
    var fixture = new Fixture();
    fixture.Loads[0].Status = "delivered";
    fixture.Loads[0].Stops[0].DeliveredAt = fixture.Now;
    fixture.Loads[1].Status = "in_transit";
    var continuation = fixture.Loads.Skip(1).ToList();

    Assert.True(FuelPlanProjection.AssignmentsMatch(fixture.Saved.Plan, fixture.Loads[1].Id, continuation));
    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(1), continuation, fixture.Leg(1), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Empty(result.RefreshReasons);
    Assert.Equal(175, result.RemainingMiles, 5);
    Assert.All(result.Stops, stop => Assert.True(stop.Number > 1));
    Assert.All(fixture.Saved.Plan.Stops, stop => Assert.Equal(0, stop.Number));
  }

  [Theory]
  [InlineData("insert")]
  [InlineData("reorder")]
  [InlineData("changed-stop")]
  [InlineData("changed-truck")]
  [InlineData("new-continuation")]
  public void ChangedRemainingAssignmentsInvalidateTheSavedPlan(string change)
  {
    var fixture = new Fixture();
    var loads = fixture.Loads.ToList();
    switch (change)
    {
      case "insert": loads.Insert(1, new() { Id = Guid.NewGuid(), TruckId = fixture.TruckId }); break;
      case "reorder": (loads[1], loads[2]) = (loads[2], loads[1]); break;
      case "changed-stop": loads[1].Stops[0].Address = "Changed pickup address"; break;
      case "changed-truck": loads[1].TruckId = Guid.NewGuid(); break;
      case "new-continuation": loads.Add(new() { Id = Guid.NewGuid(), TruckId = fixture.TruckId }); break;
    }

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("Assigned stops", StringComparison.Ordinal));
  }

  [Fact]
  public void KnownPostHorizonAssignmentMustStillBeTheImmediateContinuation()
  {
    var fixture = new Fixture();
    var next = new DispatchResponse { Id = Guid.NewGuid(), TruckId = fixture.TruckId };
    fixture.Saved.Plan.ArrivalPolicy!.NextDispatchId = next.Id;
    fixture.Saved.Plan.DispatchSignatures[next.Id] = FuelHorizon.LoadSignature(next);
    var loads = fixture.Loads.Append(next).ToList();
    Assert.True(FuelPlanProjection.AssignmentsMatch(fixture.Saved.Plan, fixture.Loads[1].Id, loads.Skip(1).ToList()));

    loads.Insert(3, new() { Id = Guid.NewGuid(), TruckId = fixture.TruckId });
    Assert.False(FuelPlanProjection.AssignmentsMatch(fixture.Saved.Plan, fixture.Loads[1].Id, loads.Skip(1).ToList()));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ChangedPostHorizonPickupOrMissingSavedSignatureInvalidatesTheEscapePolicy(bool missingSignature)
  {
    var fixture = new Fixture();
    var next = new DispatchResponse { Id = Guid.NewGuid(), TruckId = fixture.TruckId,
      Stops = [new() { Id = Guid.NewGuid(), Sequence = 1, Address = "Original pickup", Latitude = 40, Longitude = -80 }] };
    fixture.Saved.Plan.ArrivalPolicy!.NextDispatchId = next.Id;
    var loads = fixture.Loads.Append(next).ToList();
    if (!missingSignature)
    {
      fixture.Saved.Plan.DispatchSignatures[next.Id] = FuelHorizon.LoadSignature(next);
      Assert.True(FuelPlanProjection.AssignmentsMatch(fixture.Saved.Plan, fixture.Loads[0].Id, loads));
      next.Stops[0].Address = "Pickup in a different direction";
      next.Stops[0].Longitude = -75;
    }

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("Assigned stops", StringComparison.Ordinal));
  }

  [Fact]
  public void LaterVisitToTheSameStationSurvivesRolloverAndOriginalSnapshotIsUnchanged()
  {
    var fixture = new Fixture();
    var original = JsonSerializer.Serialize(fixture.Saved, RoutePlanningService.Json);

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(1), fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);

    var stop = Assert.Single(result.Stops);
    Assert.Equal(fixture.StationId, stop.StationId);
    Assert.Equal(fixture.Saved.Plan.Stops[1].VisitKey, stop.VisitKey);
    Assert.Equal(fixture.Loads[1].Id, stop.DispatchId);
    Assert.Equal(fixture.Saved.Stops[1].Stop.Id, stop.BeforeStopId);
    Assert.Equal(25, stop.MilesAhead, 5);
    Assert.Null(stop.CurrentRouteMile);
    Assert.Equal(fixture.Saved.Stops.Skip(1).Select(x => x.Stop.Id), result.StopArrivals.Select(x => x.StopId));
    Assert.Equal(result.ArrivalGallons, result.StopArrivals[^1].Gallons, 6);
    Assert.Equal(original, JsonSerializer.Serialize(fixture.Saved, RoutePlanningService.Json));
  }

  [Fact]
  public void FreshTankReadingRebasesFuelWithoutAssumingThePassedPurchaseOccurred()
  {
    var fixture = new Fixture();
    var state = fixture.State(1) with { FuelPercent = 50, FuelUpdatedAt = fixture.Now };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(50, result.StartingGallons);
    Assert.Equal(45, Assert.Single(result.Stops).ArrivalGallons);
    Assert.Equal(20, result.PurchaseGallons);
    Assert.Equal(35, result.ArrivalGallons);
    Assert.Null(result.SavingsUsd);
    Assert.Equal(60, result.PurchaseCostUsd);
    Assert.Equal(80, result.EconomicCostUsd);
    Assert.True(result.RemainingCostEstimate);
  }

  [Theory]
  [InlineData(16)]
  [InlineData(360)]
  [InlineData(2880)]
  public void ReadingAgeAloneDoesNotHideStationsOrChangePurchaseQuantities(int ageMinutes)
  {
    var fixture = new Fixture();
    var original = JsonSerializer.Serialize(fixture.Saved, RoutePlanningService.Json);
    var state = fixture.State(0) with { FuelUpdatedAt = fixture.Now.AddMinutes(-ageMinutes) };
    var fresh = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(50, result.StartingGallons);
    Assert.Equal(fixture.Saved.Plan.Stops.Select(x => x.VisitKey), result.Stops.Select(x => x.VisitKey));
    Assert.All(result.Stops, stop => Assert.True(stop.ArrivalGallons >= fixture.Profile.ReserveGallons));
    Assert.True(result.ArrivalGallons >= fixture.Saved.Plan.ArrivalPolicy!.MinimumGallons);
    Assert.Equal(fixture.Saved.Plan.Stops.Select(x => x.BuyGallons), result.Stops.Select(x => x.BuyGallons));
    Assert.Equal(JsonSerializer.Serialize(fresh), JsonSerializer.Serialize(result));
    Assert.Equal(original, JsonSerializer.Serialize(fixture.Saved, RoutePlanningService.Json));
  }

  [Fact]
  public void InsufficientFuelStillInvalidatesAnUnreachableFirstStation()
  {
    var fixture = new Fixture();
    var state = fixture.State(0) with { FuelPercent = 12 };
    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);
    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("no longer reachable", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(45)]
  [InlineData(50)]
  public void LowFuelRecoveryRemainsVisibleBeforeAndAtTheFirstPurchase(double progress)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.Stops.ForEach(x => x.BuyGallons = 30);
    var state = fixture.State(0, progress) with { FuelPercent = 2 };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh, string.Join("; ", result.RefreshReasons));
    Assert.Equal(2, result.StartingGallons);
    Assert.Equal(2, result.Stops.Count);
    Assert.InRange(result.Stops[0].ArrivalGallons, 0, 2);
    Assert.All(result.Stops, x => Assert.True(x.DepartureGallons >= fixture.Profile.ReserveGallons));
    Assert.True(result.Stops[1].ArrivalGallons >= fixture.Profile.ReserveGallons);
    Assert.True(result.ArrivalGallons >= fixture.Profile.ReserveGallons);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(.9)]
  public void LowFuelProjectionNeverPublishesAnUnreachableFirstPurchase(double fuelPercent)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.Stops.ForEach(x => x.BuyGallons = 30);
    var state = fixture.State(0, 45) with { FuelPercent = fuelPercent };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.NotEmpty(result.RefreshReasons);
  }

  [Fact]
  public void RecoveryExceptionDoesNotExtendPastTheFirstFuelPurchase()
  {
    var fixture = new Fixture();
    var state = fixture.State(0, 45) with { FuelPercent = 2 };
    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);
    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, x => x.Contains("no longer reachable", StringComparison.Ordinal));
  }

  [Fact]
  public void RemainingTotalsUseNormalizedUsdPricesAndExcludePassedPurchases()
  {
    var fixture = new Fixture();
    var remaining = fixture.Saved.Plan.Stops[1];
    remaining.Currency = "CAD";
    remaining.Unit = "L";
    remaining.YourPrice = 1.75;
    remaining.EconomicPrice = 1.5;
    remaining.CashUsdPerGallon = 5;
    remaining.EconomicUsdPerGallon = 4;
    remaining.DetourMinutes = 3;
    fixture.Saved.Plan.PurchaseCostUsd = 500;
    fixture.Saved.Plan.EconomicCostUsd = 480;
    fixture.Saved.Plan.ExpectedFutureFuelCostUsd = 80;
    fixture.Saved.Plan.ExtraMinutes = 18;
    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(1), fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);
    Assert.False(result.NeedsRefresh);
    Assert.Equal(20, result.PurchaseGallons);
    Assert.Equal(100, result.PurchaseCostUsd);
    Assert.Equal(100 + 3d / 60 * fixture.Profile.DriverHourlyCostUsd, result.EconomicCostUsd, 10);
    Assert.Equal(0, result.ExpectedFutureFuelCostUsd);
    Assert.Equal(3, result.ExtraMinutes);
    Assert.True(result.RemainingCostEstimate);
    Assert.Null(result.SavingsUsd);
  }

  [Fact]
  public void ManualQuantityAlsoOverridesAnOlderLiveObservation()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.ManualStartingFuel = true;
    fixture.Saved.Plan.FuelObservedAt = fixture.Now.AddMinutes(-2);
    var state = fixture.State(0) with { FuelPercent = 60, FuelUpdatedAt = fixture.Now.AddMinutes(-3) };
    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);
    Assert.False(result.NeedsRefresh);
    Assert.Equal(45, result.StartingGallons);
  }

  [Theory]
  [InlineData(false, 65)]
  [InlineData(true, 50)]
  public void ManualQuantityKeepsPrecedenceUntilANewerSensorReadingArrives(bool newer, double expectedGallons)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.ManualStartingFuel = true;
    fixture.Saved.Plan.StartingGallons = 70;
    fixture.Saved.Plan.FuelObservedAt = fixture.Now.AddMinutes(-2);
    var state = fixture.State(0) with
    { FuelPercent = 50, FuelUpdatedAt = newer ? fixture.Now : fixture.Now.AddMinutes(-2) };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(expectedGallons, result.StartingGallons);
  }

  [Fact]
  public void OrdinaryProjectionRetainsOnlyTheDatedScheduleSnapshotAndOmitsRouteCheckDetails()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.RouteChecks = [new() { Result = "Selected" }];
    fixture.Saved.Plan.ScheduleImpact = new(fixture.Now, true, true, false, false, 0, 0, [], null);

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.Empty(result.RouteChecks);
    Assert.NotNull(result.ScheduleImpact);
    Assert.Equal(fixture.Now, result.ScheduleImpact.CalculatedAt);
    Assert.Single(fixture.Saved.Plan.RouteChecks);
    Assert.NotNull(fixture.Saved.Plan.ScheduleImpact);
  }

  [Theory]
  [InlineData("unknown")]
  [InlineData("missing-time")]
  [InlineData("future-time")]
  [InlineData("invalid-level")]
  public void UnverifiedTankReadingsCannotValidateTheRemainingPurchases(string reading)
  {
    var fixture = new Fixture();
    var state = fixture.State(1);
    state = reading switch
    {
      "unknown" => state with { FuelPercent = null },
      "missing-time" => state with { FuelUpdatedAt = null },
      "future-time" => state with { FuelUpdatedAt = fixture.Now.AddMinutes(2) },
      _ => state with { FuelPercent = 101 }
    };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("fresh fuel reading", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(30, 25, false)]
  [InlineData(30.01, 25, true)]
  [InlineData(10, 75, true)]
  public void ManualFuelExpiresAfterThirtyMinutesOrAnUnconfirmedPassedPurchase(double minutes, double progress, bool needsRefresh)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.ManualStartingFuel = true;
    fixture.Saved.Plan.CalculatedAt = fixture.Now.AddMinutes(-minutes);
    var state = fixture.State(0, progress) with { FuelPercent = null, FuelUpdatedAt = null };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.Equal(needsRefresh, result.NeedsRefresh);
    if (!needsRefresh) Assert.Equal(45, result.StartingGallons, 5);
    else Assert.Contains(result.RefreshReasons, reason => reason.Contains("fresh fuel reading", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("off-road")]
  [InlineData("stale-gps")]
  [InlineData("missing-road")]
  public void CheckedRoadMustMatchTheCurrentFreshPosition(string location)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.StopArrivals = [new(fixture.Loads[0].Id, fixture.Saved.Stops[0].Stop.Id, 50, 50)];
    var state = fixture.State(0);
    var leg = fixture.Leg(0);
    if (location == "off-road") state = state with { Progress = state.Progress! with { Position = new(40.1, -99.75) } };
    if (location == "stale-gps") state = state with { Progress = state.Progress! with { LocationStale = true } };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, location == "missing-road" ? null : leg, fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("GPS position", StringComparison.Ordinal));
    Assert.Empty(result.StopArrivals);
  }

  [Theory]
  [InlineData("profile")]
  [InlineData("date")]
  [InlineData("selection-version")]
  [InlineData("future-version")]
  public void ChangedFuelInputsAndOldPricesCannotReuseAValidRecommendation(string changed)
  {
    var fixture = new Fixture();
    if (changed == "profile") fixture.Profile.StopCostUsd += 1;
    if (changed == "date") fixture.Saved.Plan.PricingDate = fixture.Saved.Plan.PricingDate.AddDays(-1);
    if (changed == "selection-version") fixture.Saved.Plan.SelectionVersion = FuelOptimizer.MinimumProjectionVersion - 1;
    if (changed == "future-version") fixture.Saved.Plan.SelectionVersion = FuelOptimizer.SelectionVersion + 1;

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.NotEmpty(result.RefreshReasons);
  }

  [Theory]
  [InlineData(11)]
  [InlineData(12)]
  [InlineData(13)]
  [InlineData(14)]
  [InlineData(15)]
  [InlineData(16)]
  [InlineData(17)]
  [InlineData(18)]
  [InlineData(19)]
  [InlineData(20)]
  public void SearchOrderingUpgradeKeepsThePreviouslyCheckedPlanVisible(int version)
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.SelectionVersion = version;
    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.NotEmpty(result.Stops);
    Assert.Equal(fixture.Saved.Plan.Stops.Select(x => x.BuyGallons), result.Stops.Select(x => x.BuyGallons));
    Assert.Equal(version, fixture.Saved.Plan.SelectionVersion);
  }

  [Fact]
  public async Task GeometryMemoryReusesOnlyTheRequestedSavedVersion()
  {
    var fixture = new Fixture();
    using var memory = new FuelPlanMemory();
    var calls = 0;
    Task<TruckFuelPlanSnapshot?> Load() { calls++; return Task.FromResult<TruckFuelPlanSnapshot?>(fixture.Saved); }

    var first = await memory.LegAsync(fixture.Saved, 1, Load, default);
    var second = await memory.LegAsync(fixture.Saved, 1, Load, default);
    var changed = fixture.Saved with { CalculatedAt = fixture.Saved.CalculatedAt.AddTicks(1) };
    var mismatched = await memory.LegAsync(changed, 1, Load, default);

    Assert.NotNull(first);
    Assert.Same(first, second);
    Assert.Null(mismatched);
    Assert.Equal(2, calls);
  }

  [Fact]
  public void FullFillProjectionBuysExactlyTheConfiguredFractionalTankCapacity()
  {
    var fixture = new Fixture();
    fixture.Profile.TankGallons = 211.33764189;
    fixture.Saved.Plan.ProfileSignature = JsonSerializer.Serialize(fixture.Profile, RoutePlanningService.Json);
    fixture.Saved.Plan.Stops[0].FillToTarget = true;
    fixture.Saved.Plan.Stops.RemoveAt(1);
    var state = fixture.State(0) with { FuelPercent = 20 };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh, string.Join("; ", result.RefreshReasons));
    var stop = Assert.Single(result.Stops);
    Assert.Equal(fixture.Profile.TankGallons.Value, stop.DepartureGallons, 9);
    Assert.Equal(stop.ArrivalGallons + stop.BuyGallons, stop.DepartureGallons, 9);
    Assert.Equal(stop.BuyGallons * stop.CashUsdPerGallon, result.PurchaseCostUsd, 9);
  }

  [Fact]
  public void EstimatedAccessUsesBaselineProgressAndChargesBothSidesOfEveryRemainingVisit()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    foreach (var stop in fixture.Saved.Plan.Stops)
    {
      stop.RouteMilesAhead = stop.MilesAhead;
      stop.DetourMiles = 6;
    }
    fixture.Saved.Plan.Stops[0].MilesAhead = 53;
    fixture.Saved.Plan.Stops[1].MilesAhead = 159;

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(0), fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(287, result.RemainingMiles, 5);
    Assert.Equal(28, result.Stops[0].MilesAhead, 5);
    Assert.Equal(134, result.Stops[1].MilesAhead, 5);
    Assert.Equal(44.4, result.Stops[0].ArrivalGallons, 5);
    Assert.Equal(43.2, result.Stops[1].ArrivalGallons, 5);
    Assert.Equal(32.6, result.ArrivalGallons, 5);
  }

  [Fact]
  public void EstimatedReturnVisitDoesNotUseAccumulatedAccessAsBaselineProgress()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    foreach (var stop in fixture.Saved.Plan.Stops)
    { stop.RouteMilesAhead = stop.MilesAhead; stop.DetourMiles = 6; }
    fixture.Saved.Plan.Stops[1].MilesAhead = 159;

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(1), fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);

    Assert.False(result.NeedsRefresh);
    var visit = Assert.Single(result.Stops);
    Assert.Equal(2, visit.Number);
    Assert.Equal(28, visit.MilesAhead, 5);
    Assert.Equal(25, visit.RouteMilesAhead!.Value, 5);
    Assert.Equal(181, result.RemainingMiles, 5);
    Assert.Equal(33.8, result.ArrivalGallons, 5);
  }

  [Fact]
  public void NearbyOffRoadPositionPaysItsInitialAccessWithoutRequiringNewGeometry()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    fixture.Saved.Plan.Stops.ForEach(x => x.RouteMilesAhead = x.MilesAhead);
    var state = fixture.State(0);
    state = state with { Progress = state.Progress! with { Position = new(40.0032, -99.75) } };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(.5, result.StartAccessMiles);
    Assert.Equal(25.5, result.Stops[0].MilesAhead, 5);
    Assert.Equal(44.9, result.Stops[0].ArrivalGallons, 5);
    Assert.Equal(34.9, result.ArrivalGallons, 5);
    Assert.Equal(3, result.ExtraMinutes);
    Assert.Equal(275.5, result.RemainingMiles, 5);
  }

  [Fact]
  public void OriginEstimateDoesNotAcceptAnUnmatchedCurrentPositionBeyondFortyMiles()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    fixture.Saved.Plan.Stops.ForEach(x => x.RouteMilesAhead = x.MilesAhead);
    var state = fixture.State(0);
    state = state with { Progress = state.Progress! with { Position = new(40.6, -99.75) } };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.True(result.NeedsRefresh);
    Assert.Contains(result.RefreshReasons, reason => reason.Contains("GPS position"));
  }

  [Fact]
  public void ManualOriginAllowanceIsNotLostAfterTheTruckRejoinsTheSavedRoad()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    fixture.Saved.Plan.ManualStartingFuel = true;
    fixture.Saved.Plan.StartAccessMiles = .5;
    fixture.Saved.Plan.Stops.ForEach(x => x.RouteMilesAhead = x.MilesAhead);
    var state = fixture.State(0) with { FuelUpdatedAt = fixture.Saved.CalculatedAt.AddMinutes(-1) };

    var result = FuelPlanProjection.Project(fixture.Saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Equal(44.9, result.StartingGallons, 5);
    Assert.Equal(39.9, result.Stops[0].ArrivalGallons, 5);
    Assert.Equal(0, result.StartAccessMiles);
  }

  [Fact]
  public void UnchangedManualReplayRetainsItsAccessCostsAndExactQuantitiesAfterProjection()
  {
    var fixture = new Fixture();
    var visits = fixture.Saved.Plan.Stops.Select((source, index) =>
      new FuelCandidate(source, source.MilesAhead, 3, 3, source.CashUsdPerGallon, source.EconomicUsdPerGallon)
      { LegIndex = index }).ToList();
    var edits = visits.Select(x => new FuelPlanEditStop(x.Station.StationId, x.Station.BeforeStopId, 20, false)).ToList();
    var replay = FuelManualReplay.Evaluate(300, 50, fixture.Profile, visits, edits,
      fixture.Saved.Plan.ArrivalPolicy!, 1, .5);
    Assert.Empty(replay.Errors);
    var plan = replay.Plan;
    plan.ManuallyEdited = true;
    plan.TruckId = fixture.TruckId;
    plan.DispatchIds = fixture.Saved.Plan.DispatchIds;
    plan.DispatchSignatures = fixture.Saved.Plan.DispatchSignatures;
    plan.PricingDate = fixture.Saved.Plan.PricingDate;
    plan.ProfileSignature = fixture.Saved.Plan.ProfileSignature;
    plan.CalculatedAt = fixture.Now;
    var saved = fixture.Saved with { CalculatedAt = fixture.Now, Plan = plan,
      CheckedRoute = null, BaselineRoute = fixture.Saved.CheckedRoute };
    var state = fixture.State(0, 0);
    state = state with { Progress = state.Progress! with { Position = new(40.0032, -100) } };

    var result = FuelPlanProjection.Project(saved, state, fixture.Loads, fixture.Leg(0), fixture.Now);

    Assert.False(result.NeedsRefresh, string.Join("; ", result.RefreshReasons));
    Assert.True(result.ManuallyEdited);
    Assert.Equal(plan.PurchaseCostUsd, result.PurchaseCostUsd, 10);
    Assert.Equal(plan.EconomicCostUsd, result.EconomicCostUsd, 10);
    Assert.Equal(31, result.ExtraMinutes);
    Assert.Equal(plan.ArrivalGallons, result.ArrivalGallons, 10);
    Assert.Equal(plan.RemainingMiles, result.RemainingMiles, 10);
    Assert.Equal(plan.ExpectedFutureFuelCostUsd, result.ExpectedFutureFuelCostUsd, 10);
    Assert.Equal(plan.Stops.Select(x => x.BuyGallons), result.Stops.Select(x => x.BuyGallons));
    Assert.True(result.RemainingCostEstimate);
  }

  [Fact]
  public void RemainingCostDropsPassedAccessAndDoesNotChargeHistoricalScheduleDelayAgain()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    fixture.Saved.Plan.StartAccessMiles = .5;
    foreach (var stop in fixture.Saved.Plan.Stops)
    {
      stop.RouteMilesAhead = stop.MilesAhead;
      stop.DetourMiles = 6;
      stop.DetourMinutes = FuelAccessEstimate.DrivingMinutes(6);
    }
    fixture.Saved.Plan.EconomicCostUsd = 999;
    fixture.Saved.Plan.ExtraMinutes = 31;
    fixture.Saved.Plan.ScheduleImpact = new(fixture.Now.AddMinutes(-30), true, true,
      false, false, 240, 120, [], null);

    var result = FuelPlanProjection.Project(fixture.Saved, fixture.State(1),
      fixture.Loads.Skip(1).ToList(), fixture.Leg(1), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Single(result.Stops);
    Assert.Equal(0, result.StartAccessMiles);
    Assert.Equal(14, result.ExtraMinutes);
    Assert.Equal(80 + 14d / 60 * fixture.Profile.DriverHourlyCostUsd, result.EconomicCostUsd, 10);
    Assert.Equal(240, result.ScheduleImpact!.AddedMinutes);
    Assert.Equal(fixture.Now.AddMinutes(-30), result.ScheduleImpact.CalculatedAt);
    Assert.True(result.RemainingCostEstimate);
    Assert.Null(result.SavingsUsd);
    Assert.Equal(999, fixture.Saved.Plan.EconomicCostUsd);
  }

  [Fact]
  public void NoRemainingPurchasesChargeOnlyCurrentAccessNotPastStopsOrHistoricalWait()
  {
    var fixture = new Fixture();
    fixture.Saved.Plan.EstimatedStationAccess = true;
    fixture.Saved.Plan.Stops.ForEach(x => { x.RouteMilesAhead = x.MilesAhead; x.DetourMinutes = 14; });
    fixture.Saved.Plan.EconomicCostUsd = 999;
    fixture.Saved.Plan.ScheduleImpact = new(fixture.Now.AddMinutes(-30), true, true,
      false, false, 240, 120, [], null);
    var state = fixture.State(2);
    state = state with { Progress = state.Progress! with { Position = new(40.0032, -100.25) } };

    var result = FuelPlanProjection.Project(fixture.Saved, state,
      fixture.Loads.Skip(2).ToList(), fixture.Leg(2), fixture.Now);

    Assert.False(result.NeedsRefresh);
    Assert.Empty(result.Stops);
    Assert.Equal(0, result.PurchaseCostUsd);
    Assert.Equal(.5, result.StartAccessMiles);
    Assert.Equal(3, result.ExtraMinutes);
    Assert.Equal(3d / 60 * fixture.Profile.DriverHourlyCostUsd, result.EconomicCostUsd, 10);
    Assert.True(result.RemainingCostEstimate);
  }

  private sealed class Fixture
  {
    public DateTime Now { get; } = new(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
    public Guid TruckId { get; } = Guid.NewGuid();
    public Guid StationId { get; } = Guid.NewGuid();
    public TruckRouteProfile Profile { get; } = new() { Confirmed = true, TankGallons = 100, Mpg = 5,
      ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20 };
    public List<DispatchResponse> Loads { get; } = [];
    public TruckFuelPlanSnapshot Saved { get; }

    public Fixture()
    {
      var points = new[] { new RoutePoint(40, -100), new RoutePoint(40, -99), new RoutePoint(40, -100), new RoutePoint(40, -101) };
      var route = new TruckRoute { Miles = 300, Seconds = 18000 };
      var itinerary = new List<FuelItineraryStop>();
      for (var i = 0; i < 3; i++)
      {
        var stop = new DispatchStopResponse { Id = Guid.NewGuid(), TruckId = TruckId, Sequence = 1,
          Address = $"Address {i}", Latitude = (decimal)points[i + 1].Latitude, Longitude = (decimal)points[i + 1].Longitude,
          ScheduledDate = DateOnly.FromDateTime(Now) };
        var load = new DispatchResponse { Id = Guid.NewGuid(), TruckId = TruckId, Status = "assigned", Stops = [stop] };
        Loads.Add(load);
        itinerary.Add(new(load.Id, new(stop.Id, $"Stop {i}", stop.Address, 1, points[i + 1]), (i + 1) * 100));
        route.Legs.Add(new(100, 6000, [points[i], points[i + 1]]));
      }
      var fuel = new FuelPlan { TruckId = TruckId, DispatchIds = Loads.Select(x => x.Id).ToList(),
        DispatchSignatures = Loads.ToDictionary(x => x.Id, FuelHorizon.LoadSignature),
        PricingDate = FuelPricingDate.FromUtc(Now), CalculatedAt = Now.AddMinutes(-1),
        ProfileSignature = JsonSerializer.Serialize(Profile, RoutePlanningService.Json),
        SelectionVersion = FuelOptimizer.SelectionVersion, StartingGallons = 50, RemainingMiles = 300,
        PurchaseGallons = 40, PurchaseCostUsd = 120, EconomicCostUsd = 160, SavingsUsd = 15,
        ArrivalPolicy = new() { MinimumGallons = 10, TargetGallons = 10, ReplacementPriceUsd = 3 } };
      for (var i = 0; i < 2; i++)
        fuel.Stops.Add(new() { StationId = StationId, VisitKey = $"{StationId:N}:{i}", DispatchId = Loads[i].Id,
          BeforeStopId = itinerary[i].Stop.Id, Point = new(40, -99.5), MilesAhead = 50 + i * 100,
          BuyGallons = 20, YourPrice = 3, EconomicPrice = 3, CashUsdPerGallon = 3, EconomicUsdPerGallon = 3,
          Currency = "USD", Unit = "US gal", CurrentRouteMile = 50 });
      Saved = new(TruckId, Loads[0].Id, fuel.CalculatedAt, fuel, itinerary, route);
    }

    public RouteGeometry Leg(int index) => new(new() { Legs = [Saved.CheckedRoute!.Legs[index]] });

    public RoutePlanningState State(int index, double progress = 25)
    {
      var plan = new RoutePlan { TruckId = TruckId, DispatchId = Loads[index].Id, Profile = Profile,
        Tracking = new() { NextStopId = Saved.Stops[index].Stop.Id } };
      return new(Profile, plan, new(progress, 100 - progress, 6000, 0, false, false, Now, Leg(index).At(progress)), 50, Now, true);
    }
  }
}
