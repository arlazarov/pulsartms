using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fuel;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public async Task UnreachableStationIsPublishedWithoutSavingAFictionalFuelPlan(
    double fuelPercent
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = (decimal)fuelPercent;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var result = await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    var access = result.State!.Plan!.FuelRecommendations;
    Assert.NotNull(access);
    Assert.True(access.AccessProblem);
    Assert.Equal(FuelCalculationStatus.UnreachableStation, result.FuelStatus);
    Assert.True(Assert.Single(access.Stations).ShortfallGallons > 0);
    Assert.Contains("Cannot reach", result.Message);
    Assert.Contains("US gal short", Assert.Single(access.Stations).Warning);
    Assert.Null(result.State.Plan.FuelPlan);
    var saved = SavedRouteReader.Plan(
      (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson
    )!;
    Assert.True(saved.FuelRecommendations!.AccessProblem);
    Assert.Null(saved.FuelPlan);
    Assert.Empty(await f.Db.Set<TruckFuelPlan>().ToListAsync());

    f.Location.FuelPercent = 15;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    var refreshed = await f.Plans.GetAsync(f.Load.Id, default);
    Assert.Null(refreshed.Plan!.FuelRecommendations);
    var recovered = await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    Assert.NotNull(recovered.State!.Plan!.FuelPlan);
    Assert.Null(recovered.State.Plan.FuelRecommendations);
  }

  [Fact]
  public async Task DirectFuelCommandReturnsDiagnosticWithoutFailureOrFinancials()
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = 0;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var response = await new BuildFuelPlanHandler(f.Services.Fuel).Handle(
      new(f.Load.Id, new(profile)),
      default
    );
    Assert.True(response.Success);
    Assert.Equal(
      FuelCalculationStatus.UnreachableStation,
      response.Response!.Status
    );
    Assert.Null(response.Response.Plan);
    Assert.True(
      Assert.Single(response.Response.Access!.Stations).ShortfallGallons > 0
    );
  }

  [Fact]
  public async Task ReachableFirstPurchaseBelowReserveIsAFeasibleOutcome()
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = 5;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var result = await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    Assert.Equal(FuelCalculationStatus.FeasibleBelowReserve, result.FuelStatus);
    var plan = Assert.IsType<FuelPlan>(result.State!.Plan!.FuelPlan);
    Assert.True(plan.Stops[0].ArrivalGallons >= 0);
    Assert.Contains("Below reserve", plan.Stops[0].Warning);
    Assert.Null(result.State.Plan.FuelRecommendations);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(40)]
  public async Task ChangedFuelBeforePublicationRejectsEitherOutcome(
    double percent
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = (decimal)percent;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var before = (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson;
    f.Publication.BeforeBegin = () =>
    {
      f.Location.FuelPercent = (decimal)percent + 1;
      return Task.CompletedTask;
    };
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Service.RecalculateFuelAsync(f.Load.Id, default)
    );
    Assert.Contains("telemetry changed", error.Message);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson
    );
    Assert.Empty(await f.Db.Set<TruckFuelPlan>().ToListAsync());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(40)]
  public async Task ChangedPositionBeforePublicationRejectsEitherOutcome(
    double percent
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = (decimal)percent;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var before = (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson;
    f.Publication.BeforeBegin = () =>
    {
      f.Location.Longitude -= .05m;
      f.Location.UpdatedAt = DateTime.UtcNow;
      return Task.CompletedTask;
    };
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Service.RecalculateFuelAsync(f.Load.Id, default)
    );
    Assert.Contains("telemetry changed", error.Message);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson
    );
    Assert.Empty(await f.Db.Set<TruckFuelPlan>().ToListAsync());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(40)]
  public async Task NewerObservationWithTheSameValuesStillPublishes(
    double percent
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = (decimal)percent;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    f.Publication.BeforeBegin = () =>
    {
      f.Location.UpdatedAt = f.Location.UpdatedAt.AddSeconds(30);
      f.Location.FuelUpdatedAt = f.Location.FuelUpdatedAt?.AddSeconds(30);
      return Task.CompletedTask;
    };
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    var saved = SavedRouteReader.Plan(
      (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson
    )!;
    if (percent == 0)
      Assert.True(saved.FuelRecommendations!.AccessProblem);
    else
      Assert.NotNull(saved.FuelPlan);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(40)]
  public async Task PublicationNeverWaitsForTelemetryInsideItsTransaction(
    double percent
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.FuelPercent = (decimal)percent;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    f.Sender.LocationReads.Clear();
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    Assert.Contains(f.Sender.LocationReads, read => !read.InTransaction);
    Assert.DoesNotContain(
      f.Sender.LocationReads,
      read => read.InTransaction && !read.CachedOnly
    );
  }
}
