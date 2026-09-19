using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.FuelPlanning;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelHistoricalPublicationTests
{
  [Theory]
  [InlineData(false, "endpoint")]
  [InlineData(true, "endpoint")]
  [InlineData(false, "completion")]
  [InlineData(true, "unknown")]
  [InlineData(false, "insert")]
  [InlineData(true, "cancel")]
  public async Task HistoricalChangesKeepProfileAndBothFuelCopies(
    bool manual,
    string change
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var history = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var profile = await f.PrepareCalculationAsync();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    var before = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    var settings = (
      await f.Db.TruckPlanningProfiles.AsNoTracking().SingleAsync()
    ).SettingsJson;
    var captured = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await HistoricalWorkFixture.ChangeAsync(f.Db, history, change);
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (manual)
        await f.Services.Fuel.EditAsync(
          f.Current.Id,
          new(initial.Plan!.CalculatedAt, []),
          true,
          default
        );
      else
        await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    });

    Assert.Contains("Historical truck work changed", error.Message);
    Assert.Null(f.Db.Database.CurrentTransaction);
    var saved = await new TruckFuelPlanStore(f.Db).ReadAsync(
      f.State.Plan.TruckId,
      false,
      default
    );
    Assert.Equal(initial.Plan!.CalculatedAt, saved!.Plan!.CalculatedAt);
    Assert.Equal(
      before.PlanJson,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
    Assert.Equal(
      settings,
      (
        await f.Db.TruckPlanningProfiles.AsNoTracking().SingleAsync()
      ).SettingsJson
    );
    var current = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan.TruckId,
      default
    );
    Assert.Equal(
      captured.Itinerary.InputSignature,
      current.Itinerary.InputSignature
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task OnwardArrivalPolicyRetainsItsHistoricalDependency()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var history = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    var regions = new FuelRegionPlanner(
      f.Services.FuelInputs,
      Options.Create(new FuelRegionOptions()),
      f.Services.Deadheads,
      new RouteRegionLookup()
    );
    var arrival = await regions.BuildAsync(
      f.State.Plan,
      f.State.Profile,
      [],
      0,
      default,
      suppliedInputs: work
    );
    Assert.Equal(f.Future.Id, arrival.Policy.NextDispatchId);
    Assert.NotNull(arrival.History);
    Assert.Equal(f.Future.Id, arrival.History.Snapshots.Single().Current.Id);
    await using (
      var transaction = await f.Services.Publication.BeginAsync(
        work.Itinerary,
        [arrival.History],
        default
      )
    )
      await transaction.CommitAsync();
    await HistoricalWorkFixture.ChangeAsync(f.Db, history, "endpoint");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Services.Publication.BeginAsync(
          work.Itinerary,
          [arrival.History],
          default
        )
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task HorizonDistinguishesHistoryFromCapturedNativeWork(
    bool native
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    if (native)
      await f.ReceiveCurrentAsync();

    var horizon = await f.Horizon.BuildAsync(f.State, f.State.Profile, default);

    Assert.Equal(2, horizon.DispatchIds.Count);
    if (native)
      Assert.Empty(horizon.History);
    else
    {
      var batch = Assert.Single(horizon.History);
      Assert.Equal(f.Future.Id, Assert.Single(batch.Snapshots).Current.Id);
    }
    Assert.Equal(0, f.Router.Calls);
  }
}
