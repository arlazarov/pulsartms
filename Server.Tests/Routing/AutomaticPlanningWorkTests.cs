using Application.Features.Execution.Models;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData("planned", false)]
  [InlineData("planned", true)]
  [InlineData("active", false)]
  public async Task SourceConflictDoesNotBlockAcceptedRouteCalculation(
    string status,
    bool savedFullRoute
  )
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new() { Id = Guid.NewGuid() },
      TruckId = f.Truck.Id,
      Status = status,
      Revision = 1,
      SourceReviewReason = "Trailer conflicts with another assignment.",
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          StartVisitId = f.Load.Stops[0].Id,
          EndVisitId = f.Load.Stops[^1].Id,
        },
      ],
    };
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();

    if (savedFullRoute)
      await f.Plans.BuildAsync(
        f.Load.Id,
        new(await f.Plans.ProfileAsync(f.Truck.Id, default))
        {
          ExecutionLegId = leg.Id,
        },
        default
      );
    f.Location.Longitude = -81;
    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.NotNull(result.State?.Plan);
    Assert.True(result.State.Plan.FromCurrentPosition);
    Assert.Equal(f.Load.Stops[1].Id, Assert.Single(result.State.Plan.Stops).Id);
    Assert.Equal(-81, result.State.Plan.Route.Legs[0].Points[0].Longitude);
    Assert.Equal(leg.Id, result.State.Plan.ExecutionLegId);
    Assert.Contains("Trailer conflicts", result.Message);
    Assert.True(f.Router.Calls > 0);
    await f.Db.Entry(leg).ReloadAsync();
    Assert.Equal(status, leg.Status);
    Assert.Equal(1, leg.Revision);
    Assert.Equal(
      "Trailer conflicts with another assignment.",
      leg.SourceReviewReason
    );
  }

  [Theory]
  [InlineData("assignment")]
  [InlineData("completion")]
  [InlineData("queue")]
  [InlineData("configuration")]
  public async Task ChangedWorkDuringRoutingCannotSaveEitherRoad(string change)
  {
    await using var f = await Fixture.CreateAsync();
    f.Router.BeforeCalculate = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      switch (change)
      {
        case "assignment":
          var other = new Truck
          {
            Id = Guid.NewGuid(),
            ExternalId = "other-route-truck",
          };
          f.Db.Trucks.Add(other);
          await f.Db.SaveChangesAsync();
          await f
            .Db.Dispatches.Where(x => x.Id == f.Load.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, other.Id));
          break;
        case "completion":
          await f
            .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
            .ExecuteUpdateAsync(s =>
              s.SetProperty(
                x => x.ManualCompletionRevision,
                x => x.ManualCompletionRevision + 1
              )
            );
          break;
        case "queue":
          f.Db.Dispatches.Add(
            new()
            {
              Id = Guid.NewGuid(),
              TruckId = f.Truck.Id,
              Status = "assigned",
              LoadNumber = 124,
            }
          );
          await f.Db.SaveChangesAsync();
          break;
        case "configuration":
          await f
            .Db.Trucks.Where(x => x.Id == f.Truck.Id)
            .ExecuteUpdateAsync(s =>
              s.SetProperty(
                x => x.ConfigurationRevision,
                x => x.ConfigurationRevision + 1
              )
            );
          break;
      }
    };

    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Contains("work changed", result.Message);
    Assert.Null(result.State!.Plan);
    Assert.Empty(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
    Assert.Empty(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
    Assert.Empty(await f.Db.TruckPlanningProfiles.AsNoTracking().ToListAsync());
    Assert.Equal(0, f.Sender.BoardCalls);
  }

  [Fact]
  public async Task ReroutingRejectsWorkChangesAndPreservesTheSavedPlan()
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    var before = (
      await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
    ).PlanJson;
    f.Location.Longitude = -81;
    f.Router.BeforeCalculate = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[1].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Longitude, -78m));
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Plans.AdvanceAutomaticallyAsync(
          f.Load.Id,
          default,
          forceReroute: true
        )
    );

    Assert.Contains("work changed", error.Message);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
  }

  [Fact]
  public async Task TrackingRejectsAStaleRoadWithoutCallingTheProvider()
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    var before = (
      await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
    ).PlanJson;
    var calls = f.Router.Calls;
    await f
      .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[1].Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Longitude, -78m));

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Plans.AdvanceAutomaticallyAsync(f.Load.Id, default)
    );

    Assert.Contains("no longer matches", error.Message);
    Assert.Equal(calls, f.Router.Calls);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
  }

  [Fact]
  public async Task AutomaticSelectionBypassesWarmWorkWithoutInvalidation()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Services.PlanningInputs.ReadAsync(f.Truck.Id, default);
    await f.Plans.LoadAsync(f.Load.Id, default);
    await f
      .Db.Dispatches.Where(x => x.Id == f.Load.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "completed"));
    var next = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = f.Truck.Id,
      Status = "assigned",
      LoadNumber = 124,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 40,
          Longitude = -79,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Latitude = 40,
          Longitude = -78,
        },
      ],
    };
    f.Db.Dispatches.Add(next);
    await f.Db.SaveChangesAsync();

    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Equal(next.Id, result.DispatchId);
    Assert.Null(result.Message);
    Assert.Equal(next.Id, result.State!.Plan!.DispatchId);
    Assert.DoesNotContain(
      await f.Db.DispatchRoutePlans.ToListAsync(),
      x => x.DispatchId == f.Load.Id
    );
    Assert.Equal(0, f.Sender.BoardCalls);
  }
}
