using System.Text.Json;
using Application.Features.Fuel.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Persistence;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task LateStoredRateChangesRejectAutomaticAndManualFuel(
    bool manual,
    bool initiallyMissing
  )
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      storedExchangeRates: true
    );
    var store = new FuelExchangeRateStore(
      f.Db,
      new PlanningPublicationScope(f.Db)
    );
    if (!initiallyMissing)
      await SaveRateAsync(store, .71m);
    f.Location.FuelPercent = 40;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    var preview = await f.Services.Fuel.EditAsync(
      f.Load.Id,
      new(null, null),
      false,
      default
    );
    var before = await FuelRowsAsync(f);
    var profile = await SavedProfileJsonAsync(f);
    var calls = f.Router.Calls;
    var generation = f.Services.Reads.Generation("fuel-exchange-rate");
    f.Publication.BeforeBegin = () => SaveRateAsync(store, .79m);

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

    Assert.Equal(before, await FuelRowsAsync(f));
    Assert.Equal(profile, await SavedProfileJsonAsync(f));
    Assert.Equal(.79m, (await store.ReadAsync(default))!.UsdPerCad);
    Assert.Equal(calls, f.Router.Calls);
    Assert.Equal(generation, f.Services.Reads.Generation("fuel-exchange-rate"));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExplicitFleetRateIgnoresAnUpdatedAutomaticObservation(
    bool manual
  )
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      storedExchangeRates: true
    );
    var store = new FuelExchangeRateStore(
      f.Db,
      new PlanningPublicationScope(f.Db)
    );
    await SaveRateAsync(store, .71m);
    await f.Services.Settings.SaveAsync(
      new(new() { CadToUsd = .82 }, 0),
      default
    );
    f.Location.FuelPercent = 40;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    var preview = await f.Services.Fuel.EditAsync(
      f.Load.Id,
      new(null, null),
      false,
      default
    );
    f.Publication.BeforeBegin = () => SaveRateAsync(store, .79m);

    if (manual)
      await f.Services.Fuel.EditAsync(
        f.Load.Id,
        new(preview.ExpectedCalculatedAt, [FullTankEdit(f)]),
        true,
        default
      );
    else
      await f.Service.RecalculateFuelAsync(f.Load.Id, default);

    var saved = await f.Services.FuelPlans.ReadUncachedAsync(
      f.Truck.Id,
      default
    );
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    Assert.Equal(.82, profile.CadToUsd);
    Assert.NotNull(saved);
    Assert.Equal(
      JsonSerializer.Serialize(profile, RoutePlanningService.Json),
      saved.Plan.ProfileSignature
    );
    Assert.Equal(manual, saved.Plan.ManuallyEdited);
    Assert.Equal(.79m, (await store.ReadAsync(default))!.UsdPerCad);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static async Task SaveRateAsync(
    FuelExchangeRateStore store,
    decimal value
  )
  {
    var owner = Guid.NewGuid().ToString("N");
    var now = DateTime.UtcNow;
    Assert.True(await store.AcquireAsync(owner, now, default));
    await store.SaveAsync(
      owner,
      new FuelExchangeRate(value, DateOnly.FromDateTime(now), now),
      default
    );
    await store.ReleaseAsync(owner, default);
  }
}
