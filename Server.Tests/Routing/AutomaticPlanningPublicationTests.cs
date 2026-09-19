using Application.Features.Routing.Exceptions;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task EtaCommitFailureCannotLeaveAnUncommittedMemoryResult()
  {
    var failure = new PublicationCommitFailureProbe();
    await using var f = await Fixture.CreateAsync(publicationFailure: failure);
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    f.Publication.BeforeBegin = () =>
    {
      failure.FailNextCommit = true;
      return Task.CompletedTask;
    };

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Services.Forecasts.RefreshAsync(f.Load.Id, default)
    );

    Assert.Equal("Publication commit failed.", error.Message);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Load.Id));
    Assert.Empty(
      await f.Db.Set<DispatchEtaForecast>().AsNoTracking().ToListAsync()
    );
  }

  [Theory]
  [InlineData("base")]
  [InlineData("route")]
  [InlineData("progress")]
  public async Task FinalRoutePublicationRejectsChangesAfterPreparation(
    string operation
  )
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    string? previous = null;
    if (operation == "progress")
    {
      await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
      previous = (
        await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
      ).PlanJson;
    }
    f.Location.Longitude = -81;
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "final change"));
    };

    await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (operation == "progress")
        await f.Plans.AdvanceAutomaticallyAsync(
          f.Load.Id,
          default,
          forceReroute: true
        );
      else
        await f.Plans.BuildAsync(
          f.Load.Id,
          new(profile, operation == "route", 1),
          default
        );
    });

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      previous,
      (
        await f.Db.DispatchRoutePlans.AsNoTracking().SingleOrDefaultAsync()
      )?.PlanJson
    );
    if (operation != "progress")
      Assert.Empty(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FinalFuelPublicationKeepsBothPreviousCopies(bool manual)
  {
    await using var f = await CreateFuelEditingFixtureAsync();
    var preview = await f.Services.Fuel.EditAsync(
      f.Load.Id,
      new(null, null),
      false,
      default
    );
    var previous = await FuelRowsAsync(f);
    f.Publication.BeforeBegin = async () =>
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "final change"));

    await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (manual)
        await f.Services.Fuel.EditAsync(
          f.Load.Id,
          new(preview.ExpectedCalculatedAt, [FullTankEdit(f)]),
          true,
          default
        );
      else
        await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    });

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(previous, await FuelRowsAsync(f));
  }

  [Fact]
  public async Task FinalEtaPublicationRejectsChangedWorkAndRemovesItsMemoryEntry()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    await f.Services.Forecasts.RefreshAsync(f.Load.Id, default);
    var before = (
      await f.Db.Set<DispatchEtaForecast>().AsNoTracking().SingleAsync()
    ).ForecastJson;
    var calls = f.Publication.Calls;
    f.Publication.BeforeBegin = async () =>
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "final change"));

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Load.Id, default)
    );

    Assert.Equal(calls + 1, f.Publication.Calls);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Load.Id));
    Assert.Equal(
      before,
      (
        await f.Db.Set<DispatchEtaForecast>().AsNoTracking().SingleAsync()
      ).ForecastJson
    );
  }
}
