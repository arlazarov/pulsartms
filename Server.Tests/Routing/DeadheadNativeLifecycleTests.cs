using Application.Features.Execution.Models;
using Application.Features.Routing.Background;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Queries;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public sealed partial class DeadheadGeometryRepairTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NativeConnectionBuildsPersistsAndReachesMap(bool mutate)
  {
    await using var f = await Fixture.CreateAsync(null);
    var loads = await f
      .Db.Dispatches.Include(x => x.Stops)
      .OrderBy(x => x.LoadNumber)
      .ToArrayAsync();
    var trip = new Trip { Id = Guid.NewGuid() };
    var legs = loads
      .Select(
        (load, index) =>
          new ExecutionLeg
          {
            Id = Guid.NewGuid(),
            Trip = trip,
            TruckId = load.TruckId!.Value,
            Status = index == 0 ? "active" : "planned",
            Revision = 1,
            Stops = ExecutionStopRows.Capture(load.Stops),
            Loads =
            [
              new()
              {
                Id = Guid.NewGuid(),
                DispatchId = load.Id,
                Sequence = 1,
                StartVisitId = load.Stops[0].Id,
                EndVisitId = load.Stops[^1].Id,
              },
            ],
          }
      )
      .ToArray();
    f.Db.DispatchRates.Add(
      new DispatchRate
      {
        Id = Guid.NewGuid(),
        DispatchId = loads[1].Id,
        ConnectionHash = "load-wide-rate",
        EmptyMiles = 80,
      }
    );
    f.Db.ExecutionLegs.AddRange(legs);
    await f.Db.SaveChangesAsync();
    var work = await f.Services.Routes.LoadAsync(
      loads[1].Id,
      default,
      legs[1].Id,
      legs[1].TruckId
    );
    var inputs = new SourceRoadInputs(
      f.Db,
      f.Services.Profiles,
      f.Services.DeadheadHistory,
      new ExecutionReadScope(f.Db)
    );
    var before = await inputs.ReadAsync(work.Id, default);
    if (mutate)
      f.Router.Read = async (points, ct) =>
      {
        legs[0].Revision++;
        await f.Db.SaveChangesAsync(ct);
        return Complete(points);
      };
    if (mutate)
    {
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => f.Services.Deadheads.EnsureAsync(work, f.Profile, default)
      );
      var rejected = await f.Db.DispatchDeadheads.SingleAsync(x =>
        x.ExecutionLegId == legs[1].Id
      );
      Assert.Null(rejected.RouteJson);
      Assert.Null(rejected.Miles);
      Assert.NotEqual(before, await inputs.ReadAsync(work.Id, default));
      return;
    }
    await f.Services.Deadheads.EnsureAsync(work, f.Profile, default);
    var saved = await f.Db.DispatchDeadheads.SingleAsync(x =>
      x.ExecutionLegId == legs[1].Id
    );
    Assert.Equal(legs[0].Id, saved.PreviousExecutionLegId);
    Assert.NotNull(saved.RouteJson);
    Assert.True(saved.Miles > 0);
    Assert.Equal(1, f.Router.Calls);
    await f.Services.Deadheads.EnsureAsync(work, f.Profile, default);
    Assert.Equal(1, f.Router.Calls);
    Assert.Equal(
      "load-wide-rate",
      (await f.Db.DispatchRates.SingleAsync()).ConnectionHash
    );
    Assert.NotNull(
      await f.Services.Deadheads.ReadRouteAsync(
        loads[0].Id,
        work,
        f.Profile,
        default
      )
    );
    var handler = new GetNextLoadRoutesHandler(
      new NextLoadRouteReader(f.Db),
      f.Services.DeadheadHistory,
      f.Services.Routes,
      new SourceRoadDemand(new SourceRoadStore(f.Db), TimeProvider.System),
      f.Services.Sender
    );
    var response = (
      await handler.Handle(
        new(legs[0].TruckId, loads[0].Id, CurrentExecutionLegId: legs[0].Id),
        default
      )
    ).Response!;
    Assert.NotNull(Assert.Single(response.Routes!).Deadhead);
    var unchanged = (
      await handler.Handle(
        new(legs[0].TruckId, loads[0].Id, response.Revision, legs[0].Id),
        default
      )
    ).Response!;
    Assert.True(unchanged.Unchanged);
    legs[0].Revision++;
    await f.Db.SaveChangesAsync();
    Assert.NotEqual(before, await inputs.ReadAsync(work.Id, default));
    var changed = (
      await handler.Handle(
        new(legs[0].TruckId, loads[0].Id, response.Revision, legs[0].Id),
        default
      )
    ).Response!;
    Assert.False(changed.Unchanged);
    Assert.Null(Assert.Single(changed.Routes!).Deadhead);
  }
}
