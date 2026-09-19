using System.Text.Json;
using Application.Caching;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelExchangeRateProfileTests
{
  [Theory]
  [InlineData(null)]
  [InlineData(.82)]
  public async Task FreshProfileReadsEveryStoredInputWithoutPublishingToDisplayCaches(
    double? explicitRate
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var clock = new Clock();
    var store = new MemoryFuelExchangeRateStore
    {
      Rate = new(.74m, new(2026, 9, 16), clock.GetUtcNow().UtcDateTime),
    };
    var provider = new StubFuelExchangeRateProvider();
    var rates = new FuelExchangeRateService(store, provider, reads, clock);
    var settings = new PlanningSettingsService(db, reads);
    await settings.SaveAsync(new(new(), 0), default);
    var profiles = new TruckPlanningProfileService(db, reads, settings, rates);
    var truck = new Truck { Id = Guid.NewGuid() };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var original = await profiles.GetAsync(truck.Id, default);
    Assert.Equal(.74, original.CadToUsd);
    db.TruckPlanningProfiles.Add(
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        SettingsJson = JsonSerializer.Serialize(
          new TruckRouteProfile { HeightFeet = 14, TankGallons = 100 },
          RoutePlanningService.Json
        ),
      }
    );
    var preferences = new PlanningPreferences
    {
      UseIfta = false,
      CadToUsd = explicitRate,
      FillPercent = 75,
    };
    var settingsJson = JsonSerializer.Serialize(
      preferences,
      RoutePlanningService.Json
    );
    await db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.SettingsJson, settingsJson)
    );
    await db.SaveChangesAsync();
    store.Rate = store.Rate with { UsdPerCad = .77m };

    var fresh = await profiles.GetUncachedAsync(truck.Id, default);

    Assert.Equal(14, fresh.HeightFeet);
    Assert.False(fresh.UseIfta);
    Assert.Equal(explicitRate ?? .77, fresh.CadToUsd);
    Assert.Equal(250, fresh.TankGallons);
    Assert.Equal(100, fresh.FillPercent);
    Assert.Equal(explicitRate.HasValue ? 1 : 2, store.Reads);
    Assert.Equal(0, provider.Calls);
    Assert.Equal(0, store.Saves);
    var cached = await profiles.GetAsync(truck.Id, default);
    Assert.Equal(original.HeightFeet, cached.HeightFeet);
    Assert.Equal(original.UseIfta, cached.UseIfta);
    Assert.Equal(original.CadToUsd, cached.CadToUsd);
  }

  [Theory]
  [InlineData(null)]
  [InlineData(.82)]
  public async Task ProfileUsesSavedAutomaticRateOnlyWithoutExplicitPreference(
    double? manual
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var clock = new Clock();
    var store = new MemoryFuelExchangeRateStore
    {
      Rate = new(.74m, new(2026, 9, 16), clock.GetUtcNow().UtcDateTime),
    };
    var provider = new StubFuelExchangeRateProvider();
    var rates = new FuelExchangeRateService(store, provider, reads, clock);
    var settings = new PlanningSettingsService(db, reads);
    if (manual.HasValue)
      await settings.SaveAsync(new(new() { CadToUsd = manual }, 0), default);
    var profiles = new TruckPlanningProfileService(db, reads, settings, rates);
    var truckId = Guid.NewGuid();

    var initial = await profiles.GetAsync(truckId, default);

    Assert.Equal(manual ?? .74, initial.CadToUsd);
    Assert.Equal(manual.HasValue ? 0 : 1, store.Reads);
    Assert.Equal(0, provider.Calls);
    Assert.Equal(
      manual,
      (await settings.GetAsync(default)).Preferences.CadToUsd
    );
    Assert.Empty(await db.TruckPlanningProfiles.ToListAsync());
    clock.Now = clock.Now.AddDays(8);
    var expired = await profiles.GetAsync(truckId, default);
    Assert.Equal(manual, expired.CadToUsd);
    Assert.Equal(0, provider.Calls);
    Assert.Equal(0, store.Saves);
  }

  [Fact]
  public async Task RateRefreshChangesEffectiveProfileWithoutEditingPreferencesOrRoadInputs()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var clock = new Clock();
    var store = new MemoryFuelExchangeRateStore();
    var provider = new StubFuelExchangeRateProvider
    {
      Read = _ =>
        Task.FromResult(
          new FuelExchangeRate(
            .75m,
            new(2026, 9, 16),
            clock.GetUtcNow().UtcDateTime
          )
        ),
    };
    var rates = new FuelExchangeRateService(store, provider, reads, clock);
    var settings = new PlanningSettingsService(db, reads);
    var profiles = new TruckPlanningProfileService(db, reads, settings, rates);
    var truckId = Guid.NewGuid();
    var before = await profiles.GetAsync(truckId, default);
    Assert.Null(before.CadToUsd);
    Assert.Equal(0, provider.Calls);

    Assert.True(await rates.RefreshAsync(default));

    var after = await profiles.GetAsync(truckId, default);
    Assert.Equal(.75, after.CadToUsd);
    Assert.NotEqual(
      PlanningSettingsService.Signature(before),
      PlanningSettingsService.Signature(after)
    );
    var load = new DispatchEntity { TruckId = truckId };
    Assert.Equal(
      RoutePlanningService.HashInputs(load, before),
      RoutePlanningService.HashInputs(load, after)
    );
    Assert.Equal(1, provider.Calls);
    Assert.Equal(1, store.Saves);
    Assert.Null((await settings.GetAsync(default)).Preferences.CadToUsd);
    Assert.Empty(await db.TruckPlanningProfiles.ToListAsync());
    Assert.Empty(await db.FleetPlanningSettings.ToListAsync());
  }

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now { get; set; } =
      new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
  }
}
