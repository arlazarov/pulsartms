using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData("in_transit", false)]
  [InlineData("assigned", false)]
  [InlineData("in_transit", true)]
  public async Task CurrentSingleStopBuildsFromGpsAndReusesRoadOnSubsequentPolls(
    string status,
    bool stalePlan
  )
  {
    await using var f = await Fixture.CreateAsync();
    if (stalePlan)
      await f.Service.ForTruckAsync(f.Truck.Id, default);
    f.Load.Status = status;
    var first = f.Load.Stops[0];
    f.Load.Stops.Remove(first);
    f.Db.DispatchStops.Remove(first);
    f.Load.Stops[0].Job = "Drop Off";
    await f.Db.SaveChangesAsync();
    f.Services.Reads.Invalidate("dispatch");
    f.Services.Reads.Invalidate("board");
    var calls = f.Router.Calls;

    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Null(result.Message);
    var plan = result.State!.Plan!;
    Assert.True(plan.FromCurrentPosition);
    Assert.False(plan.InputsChanged);
    Assert.Equal(f.Load.Stops[0].Id, Assert.Single(plan.Stops).Id);
    var request = f.Router.Requests[calls];
    Assert.Equal(
      new RoutePoint((double)f.Location.Latitude, (double)f.Location.Longitude),
      request[0]
    );
    Assert.Equal(plan.Stops[0].Point, request[^1]);
    Assert.Equal(calls + 1, f.Router.Calls);
    Assert.Empty(plan.Tracking.PassedStopIds);
    Assert.Null(plan.FuelPlan);
    Assert.Null((await f.Service.ForTruckAsync(f.Truck.Id, default)).Message);
    Assert.Equal(calls + 1, f.Router.Calls);
  }

  [Fact]
  public async Task CurrentSingleStopWithoutFreshGpsDoesNotCallRoutingOrSaveAnInventedOrigin()
  {
    await using var f = await Fixture.CreateAsync();
    f.Load.PlanningTruckId = f.Truck.Id;
    f.Load.PlanningFromStopId = f.Load.Stops[1].Id;
    f.Location.UpdatedAt = DateTime.UtcNow.AddMinutes(-11);
    await f.Db.SaveChangesAsync();

    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Contains("fresh truck GPS", result.Message);
    Assert.Null(result.State!.Plan);
    Assert.Equal(0, f.Router.Calls);
    Assert.Empty(await f.Db.DispatchRoutePlans.ToListAsync());
  }

  [Theory]
  [InlineData("assigned")]
  [InlineData("in_transit")]
  public async Task BackgroundSingleStopRefreshUsesAuthoritativeCurrentAssignment(
    string status
  )
  {
    await using var f = await Fixture.CreateAsync();
    f.Load.Status = status;
    f.Load.PlanningTruckId = f.Truck.Id;
    f.Load.PlanningFromStopId = f.Load.Stops[1].Id;
    await f.Db.SaveChangesAsync();

    var result = await f.Service.ForDispatchAsync(f.Load.Id, default);

    Assert.Null(result.Message);
    Assert.True(result.State!.Plan!.FromCurrentPosition);
    Assert.Single(result.State.Plan.Stops);
    Assert.Equal(1, f.Router.Calls);
  }

  [Fact]
  public async Task BackgroundRefreshDoesNotUseGpsForSingleStopBelongingToALaterLoad()
  {
    await using var f = await Fixture.CreateAsync();
    var future = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      LoadNumber = 124,
      TruckId = f.Truck.Id,
      Status = "assigned",
      ShipDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Drop Off",
          Latitude = 40,
          Longitude = -78,
        },
      ],
    };
    f.Db.Dispatches.Add(future);
    await f.Db.SaveChangesAsync();

    var result = await f.Service.ForDispatchAsync(future.Id, default);

    Assert.Contains("too few stops", result.Message);
    Assert.Null(result.State!.Plan);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task FutureSingleStopIsNotPreparedFromCurrentTruckPosition()
  {
    await using var f = await Fixture.CreateAsync();
    f.Load.Status = "assigned";
    f.Load.PlanningTruckId = f.Truck.Id;
    f.Load.PlanningFromStopId = f.Load.Stops[1].Id;
    await f.Db.SaveChangesAsync();

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Service.PrepareUpcomingAsync(f.Load.Id, default)
    );

    Assert.Contains("too few stops", error.Message);
    Assert.Equal(0, f.Router.Calls);
    Assert.Empty(await f.Db.DispatchRoutePlans.ToListAsync());
  }
}
