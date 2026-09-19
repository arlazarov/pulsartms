using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services;
using Application.Features.Routing.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(.005)]
  [InlineData(.1)]
  public async Task FuelAtPendingPickupCalculatesAndProjectsWithoutAdvancingTrackingOrRepairingRoad(double offset)
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Location.Longitude = -80.5m;
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var profile = await fixture.Plans.ProfileAsync(fixture.Truck.Id, default);
    await fixture.Plans.BuildAsync(fixture.Load.Id, new(profile, true, 1), default);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var route = JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route);
    var calls = fixture.Router.Calls;
    fixture.Location.Latitude = 40 + (decimal)offset;
    fixture.Location.Longitude = -80;
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    fixture.Location.FuelUpdatedAt = fixture.Location.UpdatedAt;

    var result = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);

    var fuel = Assert.IsType<FuelPlan>(result.State!.Plan!.FuelPlan);
    Assert.False(fuel.NeedsRefresh, string.Join("; ", fuel.RefreshReasons));
    Assert.NotEmpty(fuel.Stops);
    Assert.Equal(2, fuel.StopArrivals.Count);
    Assert.Equal(fixture.Load.Stops[0].Id, result.State.Plan.Tracking.NextStopId);
    Assert.Empty(result.State.Plan.Tracking.PassedStopIds);
    Assert.Equal(route, JsonSerializer.Serialize(result.State.Plan.Route));
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(new(40 + offset, -80), new(40, -80))),
      fuel.StartAccessMiles, 6);
    var saved = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    Assert.Equal(0, saved!.BaselineRoute!.Legs[0].Miles);
    Assert.Equal(100, saved.BaselineRoute.Legs[1].Miles);
    var read = await fixture.Reader.ForDispatchAsync(fixture.Load.Id, default);
    Assert.False(read.State!.Plan!.FuelPlan!.NeedsRefresh);
  }

  [Fact]
  public async Task ConcurrentFuelSearchesRespectTheSharedLimitAndCancelledWaitDoesNotCallRouting()
  {
    await using var first = await Fixture.CreateAsync(pickedUp: true, truckId: Guid.Parse("00000001-0000-0000-0000-000000000000"));
    await using var second = await Fixture.CreateAsync(pickedUp: true, truckId: Guid.Parse("00000002-0000-0000-0000-000000000000"));
    await using var waiting = await Fixture.CreateAsync(pickedUp: true, truckId: Guid.Parse("00000003-0000-0000-0000-000000000000"));
    foreach (var fixture in new[] { first, second, waiting })
    {
      fixture.Location.FuelPercent = 40;
      fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
      await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    }
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var active = 0;
    async Task Pause(CancellationToken ct)
    {
      if (Interlocked.Increment(ref active) == 2) bothStarted.TrySetResult();
      try { await release.Task.WaitAsync(ct); }
      finally { Interlocked.Decrement(ref active); }
    }
    first.Sender.BeforeFuel = Pause;
    second.Sender.BeforeFuel = Pause;
    var firstCalls = first.Router.Calls;
    var secondCalls = second.Router.Calls;
    var firstProfile = await first.Plans.ProfileAsync(first.Truck.Id, default);
    var secondProfile = await second.Plans.ProfileAsync(second.Truck.Id, default);
    var waitingProfile = await waiting.Plans.ProfileAsync(waiting.Truck.Id, default);
    var firstSearch = first.Services.Fuel.BuildAsync(first.Load.Id, new(firstProfile), default);
    var secondSearch = second.Services.Fuel.BuildAsync(second.Load.Id, new(secondProfile), default);
    try
    {
      await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.Equal(2, Volatile.Read(ref active));
      var calls = waiting.Router.Calls;
      var priceCalls = waiting.Sender.FuelCalls;
      using var cancelled = new CancellationTokenSource();
      var thirdSearch = waiting.Services.Fuel.BuildAsync(waiting.Load.Id, new(waitingProfile), cancelled.Token);
      Assert.False(thirdSearch.IsCompleted);
      cancelled.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thirdSearch);
      Assert.Equal(calls, waiting.Router.Calls);
      Assert.Equal(priceCalls, waiting.Sender.FuelCalls);
    }
    finally
    {
      release.TrySetResult();
      await Task.WhenAll(firstSearch, secondSearch).WaitAsync(TimeSpan.FromSeconds(10));
    }
    Assert.NotNull(await waiting.Services.Fuel.BuildAsync(waiting.Load.Id, new(waitingProfile), default)
      .WaitAsync(TimeSpan.FromSeconds(10)));
    Assert.Equal(firstCalls, first.Router.Calls);
    Assert.Equal(secondCalls, second.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExplicitFuelRecalculationUsesSavedRoadWithoutRoutingEvenWhenPricesChanged(bool changePrices)
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var initial = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var route = JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route);
    var calls = fixture.Router.Calls;
    var first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var firstFuel = first.State!.Plan!.FuelPlan!;
    Assert.Single(firstFuel.Stops);
    Assert.False(firstFuel.ReusedCheckedRoute);
    Assert.True(firstFuel.EstimatedStationAccess);
    var stored = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    Assert.Null(stored!.CheckedRoute);
    Assert.NotNull(stored.BaselineRoute);
    Assert.Equal(calls, fixture.Router.Calls);
    fixture.Location.Longitude = -79.45m;
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    fixture.Location.FuelUpdatedAt = fixture.Location.UpdatedAt;
    if (changePrices)
    {
      var discount = fixture.Stations[0].Discounts[0];
      fixture.Stations[0].Discounts[0] = discount with
      { DiscountPrice = discount.DiscountPrice + .25m, PriceAfterIfta = discount.PriceAfterIfta + .25m };
      fixture.Services.Reads.Invalidate("fuel");
    }
    var next = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var fuel = next.State!.Plan!.FuelPlan!;
    Assert.False(fuel.ReusedCheckedRoute);
    Assert.True(fuel.EstimatedStationAccess);
    if (changePrices)
    {
      Assert.NotEqual(firstFuel.PriceSignature, fuel.PriceSignature);
    }
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(route, JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route));
    Assert.Equal(initial.State!.Plan!.Version, next.State.Plan.Version);
    Assert.Equal(first.State.Plan.Version, next.State.Plan.Version);
    Assert.Equal(45, fuel.RemainingMiles, 5);
    Assert.Single(fuel.Stops);
  }

  [Fact]
  public async Task FuelCommitFailureRollsBackTheRouteWriteAndTruckSnapshotTogether()
  {
    var failure = new FuelCommitFailureProbe();
    await using var fixture = await Fixture.CreateAsync(pickedUp: true, failure: failure);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var beforeRoute = (await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson;
    var beforeTruck = await fixture.Db.Set<Domain.Entities.Fuel.TruckFuelPlan>().AsNoTracking().SingleAsync();
    var calls = fixture.Router.Calls;
    fixture.Location.FuelPercent = 45;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    failure.FailNextSnapshotWrite = true;
    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default));
    Assert.Contains("snapshot write failure", error.Message, StringComparison.Ordinal);
    Assert.Equal(1, failure.RouteWritesBeforeFailure);
    Assert.Equal(calls, fixture.Router.Calls);
    await using var independent = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(fixture.Connection).Options);
    Assert.Equal(beforeRoute, (await independent.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson);
    var afterTruck = await independent.Set<Domain.Entities.Fuel.TruckFuelPlan>().AsNoTracking().SingleAsync();
    Assert.Equal(beforeTruck.CalculatedAt, afterTruck.CalculatedAt);
    Assert.Equal(beforeTruck.SummaryJson, afterTruck.SummaryJson);
    Assert.Equal(beforeTruck.CheckedRouteJson, afterTruck.CheckedRouteJson);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelRecalculationNeverBuildsAMissingOrChangedDispatchRoute(bool changedSavedRoute)
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    if (changedSavedRoute)
    {
      await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
      await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
      fixture.Load.Stops[1].Longitude = -78.9m;
      await fixture.Db.SaveChangesAsync();
      fixture.Services.Reads.Invalidate("dispatch");
      fixture.Services.Reads.Invalidate("board");
    }
    var before = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;
    var priceCalls = fixture.Sender.FuelCalls;

    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default));

    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(priceCalls, fixture.Sender.FuelCalls);
    var after = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    Assert.Equal(changedSavedRoute ? 1 : 0, await fixture.Db.DispatchRoutePlans.CountAsync());
  }

  [Fact]
  public async Task CompletedDispatchRolloverReadsTheRemainingTruckFuelPlanWithoutAnotherSearch()
  {
    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(now);
    var priceDate = FuelPricingDate.FromUtc(now);
    FuelStationDto Station(string name, decimal longitude, decimal price) => new(Guid.NewGuid(), name, name,
      "Street", "City", "NY", "", "US", 40, longitude,
      [new("USD", "Diesel", price, price, 0, priceDate, priceDate, price, "US gal")]);
    var futureStation = Station("Next-load fuel", -78.5m, 3);
    await using var fixture = await Fixture.CreateAsync([Station("Current fuel", -79.2m, 5), futureStation,
      Station("After delivery", -77.9m, 6)], pickedUp: true);
    fixture.Location.FuelPercent = 25;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    fixture.DateCurrentLoad(today);
    var next = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 124, Status = "assigned", TruckId = fixture.Truck.Id,
      ShipDate = today.AddDays(1), Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -78.8m,
          ScheduledDate = today.AddDays(1) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -78 }] };
    fixture.Db.Dispatches.Add(next);
    await fixture.Db.SaveChangesAsync();
    var prepared = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(fixture.Load.Id, prepared.State!.Plan!.DispatchId);
    await fixture.PrepareFuelUpcomingAsync(next.Id);
    var beforeFuelCalls = fixture.Router.Calls;
    var calculated = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.Equal(beforeFuelCalls, fixture.Router.Calls);
    var originalFuel = calculated.State!.Plan!.FuelPlan!;
    Assert.Equal([fixture.Load.Id, next.Id], originalFuel.DispatchIds);
    var futurePurchase = Assert.Single(originalFuel.Stops);
    Assert.Equal(futureStation.Id, futurePurchase.StationId);
    Assert.Equal(next.Id, futurePurchase.DispatchId);
    var current = await fixture.Db.Dispatches.Include(x => x.Stops).SingleAsync(x => x.Id == fixture.Load.Id);
    current.Status = "completed";
    current.Stops.OrderBy(x => x.Sequence).Last().DeliveredAt = DateTime.UtcNow;
    next.Status = "in_transit";
    next.Stops[0].PickedUpAt = DateTime.UtcNow;
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("board");
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Location.Longitude = -78.7m;
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    fixture.Location.FuelUpdatedAt = fixture.Location.UpdatedAt;
    var advanced = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(next.Id, advanced.DispatchId);
    Assert.Null(advanced.State!.Plan!.FuelPlan);
    var calls = fixture.Router.Calls;
    var options = Microsoft.Extensions.Options.Options.Create(new Application.Features.Synchronization.Options.SynchronizationOptions());
    var reader = new PlanningReadService(fixture.Plans, new(fixture.Cache, options), fixture.Services.BoardService,
      options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var displayed = await reader.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(next.Id, displayed.DispatchId);
    var continued = Assert.IsType<FuelPlan>(displayed.State!.Plan!.FuelPlan);
    Assert.False(continued.NeedsRefresh, string.Join("; ", continued.RefreshReasons));
    Assert.Equal(originalFuel.CalculatedAt, continued.CalculatedAt);
    var remaining = Assert.Single(continued.Stops);
    Assert.Equal(next.Id, remaining.DispatchId);
    Assert.Equal(next.Stops[1].Id, remaining.BeforeStopId);
    Assert.Equal(futurePurchase.VisitKey, remaining.VisitKey);
    Assert.Equal(20, remaining.MilesAhead, 3);
    Assert.Equal(fixture.Load.Id, (await fixture.Services.FuelPlans.ReadAsync(fixture.Truck.Id, default))!.RootDispatchId);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NearbyFuelSearchExcludesDistantDiscountsAndKeepsUsefulBridgePurchasesAcrossAssignedLoads(bool cheaperCorridorFill)
  {
    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(now);
    var priceDate = FuelPricingDate.FromUtc(now);
    FuelStationDto Station(string name, decimal latitude, decimal longitude, decimal price) => new(Guid.NewGuid(), name, name,
      "Street", "City", "NY", "", "US", latitude, longitude,
      [new("USD", "Diesel", price, price, 0, priceDate, priceDate, price, "US gal")]);
    var stations = Enumerable.Range(0, 21).Select(i => Station($"Distant discount {i}", 40.75m, -89m + i * .6m, 3)).ToList();
    stations.AddRange([Station("Corridor first", 40, -86, 5), Station("Corridor last", 40, -80, 5),
      Station("After delivery", 40, -75.3m, 6)]);
    if (cheaperCorridorFill) stations.Add(Station("Corridor cheap middle", 40, -84, 3.5m));
    await using var fixture = await Fixture.CreateAsync(stations, pickedUp: true);
    fixture.Load.ShipDate = today;
    fixture.Load.DeliveryDate = today;
    fixture.Load.Stops[0].Longitude = -91;
    fixture.Load.Stops[1].Longitude = -83;
    fixture.DateCurrentLoad(today);
    fixture.Location.Longitude = -90;
    fixture.Location.FuelPercent = 97.2m / 250m * 100;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var next = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 124, Status = "assigned", TruckId = fixture.Truck.Id,
      ShipDate = today.AddDays(1), DeliveryDate = today.AddDays(1), Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -82.5m,
          ScheduledDate = today.AddDays(1) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -75.47m,
          ScheduledDate = today.AddDays(1) }] };
    fixture.Db.Dispatches.Add(next);
    await fixture.Db.SaveChangesAsync();
    await fixture.Services.Settings.SaveAsync(new(new() { MaxDetourMinutes = 30 }, 0), default);
    var prepared = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(fixture.Load.Id, prepared.State!.Plan!.DispatchId);
    await fixture.PrepareFuelUpcomingAsync(next.Id);
    var calls = fixture.Router.Calls;
    var originalRoute = JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route);

    var response = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);

    var fuel = response.State!.Plan!.FuelPlan!;
    Assert.Equal(2, fuel.DispatchIds.Count);
    Assert.Equal(1453, fuel.RemainingMiles, 4);
    Assert.True(fuel.EstimatedStationAccess);
    Assert.False(fuel.ReusedCheckedRoute);
    Assert.Equal(97.2, fuel.StartingGallons, 4);
    Assert.Equal(125, fuel.ArrivalPolicy!.MinimumGallons);
    Assert.True(fuel.ArrivalGallons >= 125);
    Assert.Equal(cheaperCorridorFill ? 3 : 2, fuel.Stops.Count);
    if (cheaperCorridorFill)
    {
      Assert.Equal("Corridor cheap middle", fuel.Stops[1].Name);
      Assert.InRange(fuel.Stops[0].BuyGallons, 10, 30);
      Assert.False(fuel.Stops[0].FillToTarget);
      Assert.True(fuel.Stops[1].BuyGallons > fuel.Stops[0].BuyGallons);
    }
    Assert.All(fuel.Stops, stop =>
    {
      Assert.StartsWith("Corridor ", stop.Name);
      Assert.True(stop.BuyGallons >= 10);
      Assert.True(stop.ArrivalGallons >= response.State.Profile.ReserveGallons);
      Assert.True(stop.DepartureGallons <= response.State.Profile.TankGallons!.Value * response.State.Profile.FillPercent / 100);
    });
    var bounds = new FuelRegionOptions();
    var saved = (await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default))!;
    Assert.Null(saved.CheckedRoute);
    Assert.Equal(1453, saved.BaselineRoute!.Miles, 4);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(originalRoute, JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route));
    var checks = saved.Plan.RouteChecks;
    Assert.InRange(checks.Count, 1, bounds.CandidateRoadChecks);
    Assert.DoesNotContain(checks.SelectMany(check => check.Stations), name => name.StartsWith("Distant discount", StringComparison.Ordinal));
    var selected = Assert.Single(checks, check => check.Result == "Selected");
    Assert.Equal(0, selected.ExtraMiles!.Value);
    Assert.Equal(0, selected.ExtraMinutes!.Value);
  }

  [Theory]
  [InlineData(40.01)]
  [InlineData(40.025)]
  public async Task NearbyAccessFuelAndTimeAreChargedOnceWithoutChangingTheSavedRoad(decimal latitude)
  {
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    var station = new FuelStationDto(Guid.NewGuid(), "nearby", "Nearby fuel", "Street", "City", "NY", "", "US", latitude, -79.2m,
      [new("USD", "Diesel", 4, 4, 0, today, today, 4, "US gal")]);
    await using var fixture = await Fixture.CreateAsync([station], pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Services.Settings.SaveAsync(new(new() { UseIfta = false, MaxDetourMinutes = 1,
      FillPercent = 100, DriverHourlyCostUsd = 35, StopCostUsd = 20 }, 0), default);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;
    var originalRoute = JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route);
    var access = FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(new(40, -79.2), new((double)latitude, -79.2)));
    var extraMiles = access * 2;
    var extraMinutes = extraMiles * 2 + 2;

    var response = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);

    Assert.NotNull(response.State!.Plan!.FuelPlan);
    var fuel = (await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default))!.Plan;
    var stop = Assert.Single(fuel.Stops);
    Assert.True(fuel.EstimatedStationAccess);
    Assert.False(fuel.ReusedCheckedRoute);
    var selected = Assert.Single(fuel.RouteChecks, check => check.Result == "Selected");
    Assert.Equal(extraMiles, selected.ExtraMiles!.Value, 6);
    Assert.Equal(extraMinutes, fuel.ExtraMinutes, 6);
    Assert.Equal(extraMiles, stop.DetourMiles, 6);
    Assert.Equal(50 + extraMiles, fuel.RemainingMiles, 6);
    Assert.Equal(0, response.State.Profile.StopCostUsd);
    Assert.Equal(fuel.PurchaseCostUsd + extraMinutes / 60 * 35, fuel.EconomicCostUsd, 6);
    Assert.True(stop.ArrivalGallons >= response.State.Profile.ReserveGallons);
    Assert.True(fuel.ArrivalGallons >= fuel.ArrivalPolicy!.MinimumGallons);
    Assert.True(stop.DepartureGallons <= response.State.Profile.TankGallons!.Value);
    Assert.Equal(calls, fixture.Router.Calls);
    var snapshot = (await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default))!;
    Assert.Null(snapshot.CheckedRoute);
    Assert.Equal(50, snapshot.BaselineRoute!.Miles, 6);
    Assert.Equal(originalRoute, JsonSerializer.Serialize((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route));
    Assert.DoesNotContain(fuel.RouteChecks, check => check.Result == "Exceeds configured detour limit");
  }

  [Theory]
  [InlineData(3.99, "On route")]
  [InlineData(2.00, "Discount detour")]
  public async Task EstimatedAccessTimeAndFuelMustBePaidForByTheDiscount(decimal discountPrice, string expectedStation)
  {
    var today = Application.Features.Routing.Algorithms.FuelPricingDate.FromUtc(DateTime.UtcNow);
    FuelStationDto Station(string name, decimal latitude, decimal price) => new(Guid.NewGuid(), name, name,
      "Street", "City", "NY", "", "US", latitude, -79.2m,
      [new("USD", "Diesel", price, price, 0, today, today, price, "US gal")]);
    await using var fixture = await Fixture.CreateAsync([Station("On route", 40, 4),
      Station("Discount detour", 40.01m, discountPrice)], pickedUp: true);
    fixture.Load.Stops[1].Longitude = -76;
    await fixture.Db.SaveChangesAsync();
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Services.Settings.SaveAsync(new(new() { UseIfta = false, FillPercent = 100,
      DriverHourlyCostUsd = 35, StopCostUsd = 20 }, 0), default);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;

    var result = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);

    Assert.NotNull(result.State!.Plan!.FuelPlan);
    var fuel = (await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default))!.Plan;
    Assert.True(fuel.EstimatedStationAccess);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(expectedStation, Assert.Single(fuel.Stops).Name);
    var selected = Assert.Single(fuel.RouteChecks, check => check.Result == "Selected");
    Assert.Contains(fuel.RouteChecks, check => check.Stations.SequenceEqual(["On route"]) && check.CostUsd.HasValue);
    var access = FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(new(40, -79.2), new(40.01, -79.2)));
    var discount = Assert.Single(fuel.RouteChecks, check => check.Stations.SequenceEqual(["Discount detour"]));
    Assert.Equal(access * 2, discount.ExtraMiles!.Value, 6);
    Assert.Equal(access * 4 + 2, discount.ExtraMinutes!.Value, 6);
    Assert.True(discount.CostUsd.HasValue || discount.Result == "Higher estimated cost before schedule replay");
    Assert.Equal(0, result.State.Profile.StopCostUsd);
    Assert.Equal(fuel.PurchaseCostUsd + fuel.ExtraMinutes / 60 * 35, fuel.EconomicCostUsd, 6);
    Assert.True(fuel.ArrivalGallons >= fuel.ArrivalPolicy!.MinimumGallons);
    Assert.All(fuel.RouteChecks.Where(check => check.CostUsd.HasValue), check => Assert.True(check.CostUsd >= selected.CostUsd));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelIncludesOverdueAssignmentsAcrossProviderFreeBuildReadAndRecalculation(bool overdueCurrent)
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    fixture.DateCurrentLoad(today.AddDays(-3));
    fixture.Load.DeliveryDate = overdueCurrent ? today.AddDays(-2) : today;
    fixture.Load.Stops[^1].ScheduledDate = fixture.Load.DeliveryDate;
    var next = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 124, Status = "assigned", Truck = fixture.Truck,
      ShipDate = today.AddDays(-1), DeliveryDate = today.AddDays(-1), Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -78.9m,
          ScheduledDate = today.AddDays(-1) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -78.8m,
          ScheduledDate = today.AddDays(-1) }] };
    fixture.Db.Dispatches.Add(next);
    await fixture.Db.SaveChangesAsync();
    await fixture.Service.ForDispatchAsync(fixture.Load.Id, default);
    await fixture.PrepareFuelUpcomingAsync(next.Id);
    var beforeFuelCalls = fixture.Router.Calls;

    var first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var fuel = first.State!.Plan!.FuelPlan!;
    Assert.Equal(new[] { fixture.Load.Id, next.Id }, fuel.DispatchIds);
    Assert.False(fuel.NeedsRefresh);
    Assert.Equal(beforeFuelCalls, fixture.Router.Calls);
    Assert.True(fuel.EstimatedStationAccess);
    var calls = fixture.Router.Calls;
    var read = overdueCurrent
      ? await fixture.Reader.ForDispatchAsync(fixture.Load.Id, default)
      : await fixture.Reader.ForTruckAsync(fixture.Truck.Id, default);
    Assert.False(read.State!.Plan!.FuelPlan!.NeedsRefresh);
    Assert.Equal(fuel.DispatchIds, read.State.Plan.FuelPlan.DispatchIds);
    Assert.Equal(calls, fixture.Router.Calls);

    var reused = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.False(reused.State!.Plan!.FuelPlan!.ReusedCheckedRoute);
    Assert.True(reused.State.Plan.FuelPlan.EstimatedStationAccess);
    Assert.False(reused.State.Plan.FuelPlan.NeedsRefresh);
    Assert.Equal(fuel.DispatchIds, reused.State.Plan.FuelPlan.DispatchIds);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task FuelHorizonIncludesAllAssignedLoadsBeyondOldMileageAndDateCutoffs()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var ids = new List<Guid> { fixture.Load.Id };
    for (var i = 0; i < 4; i++)
    {
      var load = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 124 + i, Status = "assigned", Truck = fixture.Truck,
        ShipDate = today.AddDays(2 + i * 2), DeliveryDate = today.AddDays(2 + i * 2), Stops = [
          new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -78 + i * 10,
            ScheduledDate = today.AddDays(2 + i * 2) },
          new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -70 + i * 10 }] };
      ids.Add(load.Id);
      fixture.Db.Dispatches.Add(load);
    }
    await fixture.Db.SaveChangesAsync();
    var current = (await fixture.Service.ForTruckAsync(fixture.Truck.Id, default)).State!;
    fixture.Load.ShipDate = today;
    fixture.Load.DeliveryDate = today;
    fixture.Load.Stops[0].Job = "Pick Up";
    fixture.Load.Stops[1].Job = "Drop Off";
    await fixture.Db.SaveChangesAsync();
    var baseRoutes = new BaseRouteService(fixture.Db, fixture.Router, fixture.Services.Reads);
    foreach (var id in ids.Skip(1))
    {
      var load = await fixture.Plans.LoadAsync(id, default);
      await baseRoutes.EnsureAsync(load, current.Profile, default);
      await fixture.Services.Deadheads.EnsureAsync(load, current.Profile, default);
    }
    var calls = fixture.Router.Calls;
    var horizon = new Application.Features.Routing.Services.FuelPlanning.FuelHorizon(fixture.Plans, fixture.Db,
      fixture.Services.BoardReader, fixture.Services.Deadheads);

    var result = await horizon.BuildAsync(current, current.Profile, default);

    Assert.Equal(ids, result.DispatchIds);
    Assert.Equal(3950, result.Route.Miles, 4);
    Assert.Equal(9, result.Stops.Count);
    Assert.Equal(result.Stops.Count, result.Route.Legs.Count);
    Assert.Equal(ids[^1], result.Itinerary[^1].DispatchId);
    Assert.Equal(100, current.Plan!.Route.Miles);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(2)]
  [InlineData(3)]
  public async Task ManualFuelIncludesAssignedTripsAndCommitsAllDispatchesWithoutChangingCurrentDistance(int futureCount)
  {
    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(now);
    var priceDate = FuelPricingDate.FromUtc(now);
    var station = new FuelStationDto(Guid.NewGuid(), "terminal", "Fuel", "Street", "City", "NY", "", "US", 40, -77m,
      [new("USD", "Diesel", 4, 3.5m, .5m, priceDate, priceDate, 3, "US gal")]);
    await using var fixture = await Fixture.CreateAsync([station], pickedUp: true);
    fixture.Location.FuelPercent = 68;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    fixture.DateCurrentLoad(today);
    for (var i = 1; i <= futureCount; i++)
      fixture.Db.Dispatches.Add(new Dispatch { Id = Guid.NewGuid(), LoadNumber = 123 + i, Status = "assigned", Truck = fixture.Truck,
        ShipDate = today.AddDays(i), DeliveryDate = today.AddDays(i), Stops = [
          new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -79.8m + i, ScheduledDate = today.AddDays(i) },
          new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -79m + i }] });
    await fixture.Db.SaveChangesAsync();
    var before = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(fixture.Load.Id, before.State!.Plan!.DispatchId);
    var futureIds = await fixture.Db.Dispatches.Where(load => load.Id != fixture.Load.Id)
      .OrderBy(load => load.ShipDate).Select(load => load.Id).ToListAsync();
    foreach (var id in futureIds) await fixture.PrepareFuelUpcomingAsync(id);
    var calls = fixture.Router.Calls;
    var result = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var plan = result.State!.Plan!;
    Assert.Equal(futureCount + 1, plan.FuelPlan!.DispatchIds.Count);
    Assert.Equal(50 + futureCount * 100, plan.FuelPlan.RemainingMiles, 5);
    Assert.Equal(before.State!.Plan!.Version, plan.Version);
    Assert.Equal(100, plan.Route.Miles);
    Assert.Equal(50, result.State.Progress!.RemainingMiles!.Value, 5);
    if (futureCount == 2) Assert.Empty(plan.FuelPlan.Stops);
    else
    {
      var purchase = Assert.Single(plan.FuelPlan.Stops);
      Assert.Equal(25, purchase.BuyGallons);
      Assert.Contains(purchase.DispatchId, futureIds);
    }
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(await new TruckFuelPlanStore(fixture.Db).ReadAsync(fixture.Truck.Id, true, default));
    Assert.Equal(plan.FuelPlan.DispatchIds, saved.Plan.DispatchIds);
    Assert.Equal(1 + futureCount * 2, saved.Stops.Count);
    Assert.Null(saved.CheckedRoute);
    Assert.Equal(saved.Stops.Count, saved.BaselineRoute!.Legs.Count);
    Assert.True(saved.Plan.EstimatedStationAccess);
    Assert.Equal(calls, fixture.Router.Calls);
    var compatibility = JsonSerializer.Deserialize<RoutePlan>(
      (await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync(x => x.DispatchId == fixture.Load.Id)).PlanJson,
      RoutePlanningService.Json)!;
    Assert.Equal(saved.Plan.DispatchIds, compatibility.FuelPlan!.DispatchIds);
  }

  [Fact]
  public async Task KnownFuelUsesEightHundredLitersAndThirtyFiveLitersPerHundredKm()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var result = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.Equal(250, result.State!.Profile.TankGallons!.Value, 5);
    Assert.Equal(235.214583 / 35, result.State.Profile.Mpg!.Value, 5);
    Assert.NotNull(result.State.Plan!.FuelPlan);
    Assert.True(result.State.Plan.FuelPlan.ArrivalGallons >= result.State.Plan.FuelPlan.ArrivalPolicy!.MinimumGallons);
    Assert.Single(result.State.Plan.FuelPlan.DispatchIds);
  }

  [Fact]
  public async Task SavedFuelSurvivesAgeFuelChangesAndPassingStationWithoutPaidCalls()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var saved = first.State!.Plan!;
    saved.CalculatedAt = saved.FuelPlan!.CalculatedAt = DateTime.UtcNow.AddDays(-2);
    saved.FuelPlan.Stops.Add(new() { StationId = Guid.NewGuid(), MilesAhead = 1, BuyGallons = 30 });
    await fixture.StoreAsync(saved);
    var calls = fixture.Router.Calls;
    fixture.Location.Longitude = -79.4m;
    fixture.Location.FuelPercent = 95;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(saved.FuelPlan.CalculatedAt, repeat.State!.Plan!.FuelPlan!.CalculatedAt);
    Assert.True(repeat.State.Plan.FuelPlan.NeedsRefresh);
    Assert.Equal(saved.FuelPlan.Stops.Count, repeat.State.Plan.FuelPlan.Stops.Count);
    Assert.Equal(40, repeat.State.Progress!.RemainingMiles!.Value, 3);
    fixture.Location.UpdatedAt = DateTime.UtcNow.AddHours(-1);
    Assert.True((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.FuelPlan!.NeedsRefresh);
  }

  [Fact]
  public async Task ManualFuelRefreshSavesCurrentTankWithoutReplacingDispatchRoute()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var saved = first.State!.Plan!;
    saved.CalculatedAt = saved.FuelPlan!.CalculatedAt = DateTime.UtcNow.AddDays(-2);
    await fixture.StoreAsync(saved);
    fixture.Location.FuelPercent = 60;
    fixture.Cache.Set($"automatic-planning-error:{fixture.Load.Id}:{PlanningSettingsService.Signature(first.State.Profile)}", "Old failure");
    var refreshed = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.Equal(saved.Version, refreshed.State!.Plan!.Version);
    Assert.Equal(saved.CalculatedAt, refreshed.State.Plan.CalculatedAt);
    Assert.Equal(250 * .6, refreshed.State.Plan.FuelPlan!.StartingGallons, 5);
    Assert.True(refreshed.State.Plan.FuelPlan.CalculatedAt > saved.FuelPlan.CalculatedAt);
    Assert.Empty(refreshed.State.Plan.Route.Points);
    Assert.NotEmpty((await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!.Route.Legs[0].Points);
    var calls = fixture.Router.Calls;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(repeat.Message);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task FailedManualRefreshRetainsSavedPlanWithoutCertifyingMissingFuel()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.NotNull(first.State!.Plan!.FuelPlan);
    var calculatedAt = first.State.Plan.FuelPlan.CalculatedAt;
    var calls = fixture.Router.Calls;
    fixture.Location.FuelPercent = null;
    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default));
    Assert.Equal(calls, fixture.Router.Calls);
    var retained = (await fixture.Plans.GetAsync(fixture.Load.Id, default)).Plan!;
    Assert.NotNull(retained.FuelPlan);
    Assert.Equal(calculatedAt, retained.FuelPlan.CalculatedAt);
    Assert.Null(retained.FuelRecommendations);
    var durable = await fixture.Services.FuelPlans.ReadAsync(fixture.Truck.Id, default);
    Assert.Equal(calculatedAt, durable!.CalculatedAt);
    var options = Microsoft.Extensions.Options.Options.Create(new Application.Features.Synchronization.Options.SynchronizationOptions());
    var reader = new PlanningReadService(fixture.Plans, new(fixture.Cache, options),
      fixture.Services.BoardService, options, fixture.Services.Eta, fixture.Services.FuelPlans);
    var displayed = (await reader.ForTruckAsync(fixture.Truck.Id, default)).State!.Plan!.FuelPlan!;
    Assert.Equal(calculatedAt, displayed.CalculatedAt);
    Assert.True(displayed.NeedsRefresh);
    Assert.Contains(displayed.RefreshReasons, reason => reason.Contains("fuel reading", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task ExplicitCalculationKeepsOldTimestampedFuelWithoutInventedConsumption()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 80;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow.AddDays(-2);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var result = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.Equal(250 * .8, result.State!.Plan!.FuelPlan!.StartingGallons, 5);
    Assert.False(result.State.Plan.FuelPlan.NeedsRefresh);
  }

  [Fact]
  public async Task OnePercentFuelCanRecoverAtANearbyStationWithoutRoutingAndRemainVisibleOnReadAndRecalculation()
  {
    var today = Application.Features.Routing.Algorithms.FuelPricingDate.FromUtc(DateTime.UtcNow);
    var nearby = new FuelStationDto(Guid.NewGuid(), "nearby", "Nearby fuel", "Street", "City", "NY", "", "US",
      40, -79.49m, [new("USD", "Diesel", 4, 4, 0, today, today, 4, "US gal")]);
    await using var fixture = await Fixture.CreateAsync([nearby], pickedUp: true);
    fixture.Location.FuelPercent = 1;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;

    var response = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var fuel = Assert.IsType<FuelPlan>(response.State!.Plan!.FuelPlan);

    Assert.False(fuel.NeedsRefresh, string.Join("; ", fuel.RefreshReasons));
    Assert.True(fuel.EstimatedStationAccess);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(response.State.Profile.TankGallons!.Value * .01, fuel.StartingGallons, 5);
    var stop = Assert.Single(fuel.Stops);
    Assert.Equal(nearby.Id, stop.StationId);
    Assert.InRange(stop.ArrivalGallons, 0, 2.5);
    Assert.True(stop.DepartureGallons >= response.State.Profile.ReserveGallons);
    Assert.True(fuel.ArrivalGallons >= fuel.ArrivalPolicy!.MinimumGallons);
    var read = await fixture.Reader.ForDispatchAsync(fixture.Load.Id, default);
    Assert.False(read.State!.Plan!.FuelPlan!.NeedsRefresh);
    Assert.Equal(calls, fixture.Router.Calls);
    var reused = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.False(reused.State!.Plan!.FuelPlan!.ReusedCheckedRoute);
    Assert.True(reused.State.Plan.FuelPlan.EstimatedStationAccess);
    Assert.False(reused.State.Plan.FuelPlan.NeedsRefresh);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public async Task EmptyOrUnreachableLowFuelDoesNotPublishACompleteFuelPlan(int fuelPercent)
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = fuelPercent;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;

    var error = await Assert.ThrowsAsync<RoutePlanningException>(() =>
      fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default));

    Assert.Contains("refueling before driving", error.Message, StringComparison.Ordinal);
    Assert.Null(await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task MissingOrFutureFuelTimestampDoesNotSpendSearchBudgetOrReplaceSavedPlan(bool future)
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 80;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow.AddDays(-2);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var before = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    var calls = fixture.Router.Calls;
    fixture.Location.FuelUpdatedAt = future ? DateTime.UtcNow.AddMinutes(2) : null;

    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default));

    Assert.Equal(calls, fixture.Router.Calls);
    var after = await fixture.Services.FuelPlans.ReadCheckedAsync(fixture.Truck.Id, default);
    Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
  }

  [Theory]
  [InlineData("fuel")]
  [InlineData("fuel-age")]
  [InlineData("gps")]
  public async Task ManualFuelResponseRevalidatesLatestTelemetryLikeOrdinarySavedRead(string changedTelemetry)
  {
    var boundary = new FuelCommitFailureProbe();
    await using var fixture = await Fixture.CreateAsync(pickedUp: true, failure: boundary);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var callbackRan = false;
    boundary.SnapshotWritten = () =>
    {
      callbackRan = true;
      if (changedTelemetry == "fuel") fixture.Location.FuelPercent = null;
      else if (changedTelemetry == "fuel-age") fixture.Location.FuelUpdatedAt = DateTime.UtcNow.AddHours(-1);
      else fixture.Location.UpdatedAt = DateTime.UtcNow.AddHours(-1);
    };
    var response = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.True(callbackRan);
    var immediate = Assert.IsType<FuelPlan>(response.State!.Plan!.FuelPlan);
    Assert.Equal(changedTelemetry != "fuel-age", immediate.NeedsRefresh);
    Assert.Equal(changedTelemetry == "fuel-age", immediate.RefreshReasons.Count == 0);
    var calls = fixture.Router.Calls;
    var savedRead = await fixture.Reader.ForDispatchAsync(fixture.Load.Id, default);
    var ordinary = Assert.IsType<FuelPlan>(savedRead.State!.Plan!.FuelPlan);
    Assert.Equal(changedTelemetry != "fuel-age", ordinary.NeedsRefresh);
    Assert.Equal(immediate.CalculatedAt, ordinary.CalculatedAt);
    Assert.Equal(immediate.RefreshReasons, ordinary.RefreshReasons);
    Assert.Equal(immediate.Stops.Select(x => x.VisitKey), ordinary.Stops.Select(x => x.VisitKey));
    Assert.Equal(calls, fixture.Router.Calls);
    var durable = await fixture.Services.FuelPlans.ReadAsync(fixture.Truck.Id, default);
    Assert.Equal(immediate.CalculatedAt, durable!.CalculatedAt);
    Assert.False(durable.Plan.NeedsRefresh);
  }

  [Fact]
  public async Task SmallDetourDoesNotReplaceRouteOrFuelPlan()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    var calls = fixture.Router.Calls;
    fixture.Location.Latitude = 40.02m;
    fixture.Location.UpdatedAt = DateTime.UtcNow.AddMinutes(-3);
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.True(repeat.State!.Progress!.OffRoute);
    Assert.Equal(first.State!.Plan!.Version, repeat.State.Plan!.Version);
    Assert.True(repeat.State.Plan.FuelPlan!.NeedsRefresh);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task OldFallbackStationsDoNotGetRecalculatedByTheClock()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var saved = first.State!.Plan!;
    saved.FuelRecommendations = new() { CalculatedAt = DateTime.UtcNow.AddDays(-2) };
    saved.CalculatedAt = DateTime.UtcNow.AddDays(-2);
    await fixture.StoreAsync(saved);
    var calls = fixture.Router.Calls;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    await fixture.Service.PrepareUpcomingAsync(fixture.Load.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Null(repeat.State!.Plan!.FuelRecommendations);
  }

  [Fact]
  public async Task UpcomingRouteIsPreparedWithoutAdvancingStopsFromCurrentGps()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.Service.PrepareUpcomingAsync(fixture.Load.Id, default);
    var state = await fixture.Plans.GetAsync(fixture.Load.Id, default);
    Assert.NotNull(state.Plan);
    Assert.False(state.Plan.FromCurrentPosition);
    Assert.Empty(state.Plan.Tracking.PassedStopIds);
    Assert.Equal(1, fixture.Router.MainCalls);
    var calls = fixture.Router.Calls;
    await fixture.Service.PrepareUpcomingAsync(fixture.Load.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task IftaComesFromSettingsAndOnlyChangesOnManualRecalculation()
  {
    var today = Application.Features.Routing.Algorithms.FuelPricingDate.FromUtc(DateTime.UtcNow);
    FuelStationDto Station(string name, decimal longitude, decimal pump, decimal ifta) => new(Guid.NewGuid(), name, name, "", "", "NY", "", "US", 40, longitude,
      [new("USD", "Diesel", pump, pump, 0, today, today, ifta, "US gal")]);
    var stations = new List<FuelStationDto> { Station("Nearest", -79.4m, 4.4m, 4.3m), Station("A", -79.3m, 4, 3.9m),
      Station("B", -79.2m, 4.1m, 3), Station("C", -79.1m, 3.9m, 3.8m) };
    await using var fixture = await Fixture.CreateAsync(stations, pickedUp: true);
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    first = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.True(first.State!.Plan!.FuelPlan!.UsesIfta);
    var calls = fixture.Router.Calls;
    await fixture.Services.Settings.SaveAsync(new(new() { UseIfta = false }, 0), default);
    var updated = await fixture.Plans.GetAsync(fixture.Load.Id, default);
    Assert.False(updated.Profile.UseIfta);
    Assert.False(updated.Plan!.InputsChanged);
    Assert.True(updated.Plan.FuelPlan!.NeedsRefresh);
    var next = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.True(next.State!.Plan!.FuelPlan!.UsesIfta);
    next = await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
    Assert.False(next.State!.Plan!.FuelPlan!.UsesIfta);
    Assert.Equal(first.State.Plan.Version, next.State.Plan.Version);
  }

  [Fact]
  public async Task LegacyDetourPreferenceDoesNotTriggerAutomaticFuelSearchOrRoadRebuild()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Router.DetourExtraMinutes = 6;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(first.State!.Plan!.FuelRecommendations);
    await fixture.Services.Settings.SaveAsync(new(new() { MaxDetourMinutes = 5 }, 0), default);
    var next = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(next.State!.Plan!.FuelRecommendations);
    Assert.Equal(1, fixture.Router.MainCalls);
  }

  [Fact]
  public async Task TruckSelectionBuildsOnlyRouteAndDoesNotSearchFuel()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(fixture.Load.Id, first.DispatchId);
    Assert.True(first.State!.Profile.UsesFleetDefaults);
    Assert.False(first.State.Profile.Confirmed);
    Assert.Equal(53, first.State.Profile.TrailerLengthFeet);
    Assert.Equal(72, first.State.Profile.LengthFeet);
    Assert.Equal(250, first.State.Profile.TankGallons!.Value, 5);
    Assert.Null(first.State.Plan!.FuelPlan);
    Assert.Null(first.State.Plan.FuelRecommendations);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(50, first.State.Progress!.RemainingMiles!.Value, 3);
    Assert.Contains(fixture.Load.Stops[0].Id, first.State.Plan.Tracking.PassedStopIds);
    Assert.NotNull((await fixture.Db.DispatchStops.AsNoTracking().OrderBy(x => x.Sequence).FirstAsync()).PickedUpAt);
    fixture.Db.ChangeTracker.Clear();
    fixture.Location.Longitude = -79.4m;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(first.State.Plan.Version, repeat.State!.Plan!.Version);
    Assert.Equal(40, repeat.State.Progress!.RemainingMiles!.Value, 3);
    Assert.Null(repeat.State.Plan.FuelRecommendations);
    fixture.Location.Longitude = -79.1m;
    var passed = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(passed.State!.Plan!.FuelRecommendations);
    Assert.Equal(1, fixture.Router.Calls);
  }

  [Fact]
  public async Task OffRouteTruckRoutesFromGpsToDeliveryWithoutReturningToPickupOrRepeatingPaidCalls()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    fixture.Location.Latitude = 40.1m;
    fixture.Location.Longitude = -79.1m;
    fixture.Location.UpdatedAt = DateTime.UtcNow.AddMinutes(-3);
    var pending = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.True(pending.State!.Progress!.OffRoute);
    Assert.False(pending.State.Plan!.FromCurrentPosition);
    fixture.Db.ChangeTracker.Clear();
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    var result = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.False(result.State!.Progress!.OffRoute);
    Assert.Equal(Math.Sqrt(2) * 10, result.State.Progress.RemainingMiles!.Value, 5);
    Assert.True(result.State.Plan!.FromCurrentPosition);
    Assert.Equal(fixture.Load.Stops[1].Id, Assert.Single(result.State.Plan.Stops).Id);
    Assert.Equal(100, result.State.Plan.OriginalPlannedMiles);
    Assert.Null(result.State.Plan!.FuelRecommendations);
    Assert.Equal(2, fixture.Router.Calls);
    fixture.Db.ChangeTracker.Clear();
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(result.State.Plan.Version, repeat.State!.Plan!.Version);
    Assert.Equal(2, fixture.Router.Calls);
    Assert.Single(repeat.State.Plan.Tracking.PassedStopIds);
  }

  [Fact]
  public async Task ApproachingPickupFromBeyondItRoutesBackToPickupBeforeDelivery()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Location.Speed = 60;
    fixture.Location.Heading = 270;
    fixture.Location.UpdatedAt = DateTime.UtcNow.AddMinutes(-4);
    var pending = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.False(pending.State!.Plan!.FromCurrentPosition);
    fixture.Location.UpdatedAt = DateTime.UtcNow;
    var result = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.True(result.State!.Plan!.FromCurrentPosition);
    Assert.Empty(result.State.Plan.Tracking.PassedStopIds);
    Assert.Equal(fixture.Load.Stops[0].Id, result.State.Plan.Stops[0].Id);
    Assert.Equal(2, result.State.Plan.Stops.Count);
    var calls = fixture.Router.Calls;
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(result.State.Plan.Version, repeat.State!.Plan!.Version);
  }

  [Fact]
  public async Task CompletedCurrentLoadAutomaticallySelectsTheNextAssignedLoad()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Load.Stops[1].DeliveredAt = DateTime.UtcNow;
    var next = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 456, Status = "assigned", Truck = fixture.Truck,
      ShipDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -78.8m, ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) },
        new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", Latitude = 40, Longitude = -78 }] };
    fixture.Db.Dispatches.Add(next);
    await fixture.Db.SaveChangesAsync();
    fixture.Location.Longitude = -78.9m;
    var result = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(next.Id, result.DispatchId);
    Assert.Empty(result.State!.Plan!.Tracking.PassedStopIds);
    Assert.True(result.State.Plan.FromCurrentPosition);
    Assert.Equal(next.Stops[0].Id, result.State.Plan.Stops[0].Id);
    Assert.Equal(result.State.Plan.Route.Miles, result.State.Plan.OriginalPlannedMiles);
    Assert.NotNull(result.State.Progress!.RemainingMiles);
    Assert.NotNull(fixture.Load.Stops[1].DeliveredAt);
  }

  [Fact]
  public async Task TruckWithoutUpcomingLoadMakesNoProviderCalls()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Load.Status = "completed";
    await fixture.Db.SaveChangesAsync();
    var result = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(result.State);
    Assert.Null(result.DispatchId);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task RouteFailureIsRetriedWithBackoffInsteadOfChargingOnEveryPoll()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Router.Fail = true;
    var first = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var repeat = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.NotNull(first.Message);
    Assert.Equal(first.Message, repeat.Message);
    Assert.Equal(1, fixture.Router.Calls);
  }

  [Fact]
  public async Task UniqueStopAssignmentWorksWithoutChangingTheImportedLoad()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Load.TruckId = null;
    fixture.Load.Truck = null;
    fixture.Load.Stops.ForEach(x => x.TruckId = fixture.Truck.Id);
    await fixture.Db.SaveChangesAsync();
    var result = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    Assert.NotNull(result.State!.Plan);
    Assert.Null((await fixture.Db.Dispatches.AsNoTracking().SingleAsync()).TruckId);
  }

  private sealed class Fixture : IAsyncDisposable
  {
    public required SqliteConnection Connection;
    public required AppDbContext Db;
    public required Truck Truck;
    public required Dispatch Load;
    public required TruckLocation Location;
    public required FakeRouter Router;
    public required MemoryCache Cache;
    public required AutomaticPlanningService Service;
    public required PlanningReadService Reader;
    public required RoutePlanningService Plans;
    public required PlanningTestServices Services;
    public required List<FuelStationDto> Stations;
    public required Sender Sender;
    public void DateCurrentLoad(DateOnly date)
    {
      Load.ShipDate = date;
      Load.DeliveryDate = date;
      Load.Stops[0].Job = "Pick Up";
      Load.Stops[^1].Job = "Drop Off";
      Load.Stops.ForEach(stop => stop.ScheduledDate = date);
    }
    public async Task PrepareFuelUpcomingAsync(Guid id)
    {
      await Service.PrepareUpcomingAsync(id, default);
      await Services.Deadheads.EnsureAsync(await Plans.LoadAsync(id, default), await Plans.ProfileAsync(Truck.Id, default), default);
    }
    public async Task StoreAsync(RoutePlan plan)
    {
      var entity = await Db.DispatchRoutePlans.SingleAsync(x => x.DispatchId == Load.Id);
      entity.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      Services.Reads.Invalidate($"route:{Load.Id}");
    }
    public static async Task<Fixture> CreateAsync(List<FuelStationDto>? stations = null, bool pickedUp = false,
      FuelCommitFailureProbe? failure = null, Guid? truckId = null)
    {
      var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
      var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
      if (failure is not null) options.AddInterceptors(failure);
      var db = new AppDbContext(options.Options);
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck { Id = truckId ?? Guid.NewGuid(), ExternalId = "auto-truck", UnitNumber = "123", IsActive = true };
      var load = new Dispatch { Id = Guid.NewGuid(), LoadNumber = 123, Status = "in_transit", Truck = truck, Stops = [
        new() { Id = Guid.NewGuid(), Sequence = 1, Latitude = 40, Longitude = -80 },
        new() { Id = Guid.NewGuid(), Sequence = 2, Latitude = 40, Longitude = -79 }] };
      if (pickedUp) load.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
      db.Dispatches.Add(load); await db.SaveChangesAsync();
      var location = new TruckLocation { TruckId = truck.Id, Latitude = 40, Longitude = -79.5m,
        UpdatedAt = DateTime.UtcNow, FuelPercent = null, FuelUpdatedAt = null };
      var today = Application.Features.Routing.Algorithms.FuelPricingDate.FromUtc(DateTime.UtcNow);
      var station = new FuelStationDto(Guid.NewGuid(), "station", "Test fuel", "Street", "City", "NY", "", "US", 40, -79.2m,
        [new("USD", "Diesel", 4, 3.5m, .5m, today, today, 3, "US gal")]);
      stations ??= [station];
      var sender = new Sender(location, stations);
      var router = new FakeRouter();
      var services = new PlanningTestServices(db, router, sender);
      var plans = services.Routes;
      var cache = new MemoryCache(new MemoryCacheOptions());
      var synchronization = Microsoft.Extensions.Options.Options.Create(new Application.Features.Synchronization.Options.SynchronizationOptions());
      var reader = new PlanningReadService(plans, new(cache, synchronization), services.BoardService, synchronization,
        services.Eta, services.FuelPlans);
      return new() { Connection = connection, Db = db, Truck = truck, Load = load, Location = location,
        Router = router, Cache = cache, Plans = plans, Services = services, Stations = stations, Sender = sender,
        Reader = reader, Service = new(plans, services.Fuel, services.BoardService, cache, reader) };
    }
    public async ValueTask DisposeAsync() { Services.Dispose(); Cache.Dispose(); await Db.DisposeAsync(); await Connection.DisposeAsync(); }
  }

  private sealed class FakeRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls;
    public int MainCalls;
    public double DetourExtraMinutes;
    public List<RoutePoint[]> Requests { get; } = [];
    public bool Fail;
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct)
    {
      Calls++;
      Requests.Add(points.ToArray());
      if (points.Count == 2) MainCalls++;
      if (Fail) throw new RoutePlanningException("Route service unavailable.");
      var legs = points.Zip(points.Skip(1), (a, b) => {
        var miles = Math.Sqrt(Math.Pow(a.Longitude - b.Longitude, 2) + Math.Pow(a.Latitude - b.Latitude, 2)) * 100;
        return new RouteLeg(miles, miles * 60 + (points.Count > 2 ? DetourExtraMinutes * 60 / (points.Count - 1) : 0), [a, b]);
      }).ToList();
      return Task.FromResult(new TruckRoute { Miles = legs.Sum(x => x.Miles), Seconds = legs.Sum(x => x.Seconds),
        Points = points.ToList(), Legs = legs });
    }
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new InvalidOperationException();
  }

  private sealed class Sender(TruckLocation location, List<FuelStationDto> stations) : ISender
  {
    public int FuelCalls;
    public Func<CancellationToken, Task>? BeforeFuel;
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
      if (request is GetFuelStationsQuery)
      {
        FuelCalls++;
        if (BeforeFuel is { } before) await before(ct);
      }
      object result = request switch
      {
        GetFleetLocationsQuery => RequestResponse<FleetLocationsResponse>.Ok(new() { Trucks = [location] }),
        GetFuelStationsQuery => RequestResponse<List<FuelStationDto>>.Ok(stations),
        _ => throw new NotSupportedException()
      };
      return (TResponse)result;
    }
    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
  }
}
