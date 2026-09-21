using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class FleetFuelDefaultsTests
{
  private static readonly Guid SettingsId = new(
    "6f65ae4c-a62e-47cf-b84b-e1d5f89b908f"
  );

  [Fact]
  public async Task MissingSettingsUseFleetDefaultsWithoutCreatingAStoredRow()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var services = new PlanningTestServices(db);

    var state = await services.Settings.GetAsync(default);
    var profile = await new TruckPlanningProfileService(
      db,
      services.Reads,
      services.Settings,
      services.ExchangeRates
    ).GetAsync(Guid.NewGuid(), default);

    AssertFuelDefaults(state.Preferences);
    AssertFuelDefaults(PlanningPreferences.From(profile));
    Assert.True(state.Preferences.UseIfta);
    Assert.Equal(0, state.Preferences.StopCostUsd);
    Assert.Equal(15, state.Preferences.MaxDetourMinutes);
    Assert.Null(state.Preferences.CadToUsd);
    Assert.Equal(0, state.Revision);
    Assert.Null(state.UpdatedAt);
    Assert.Empty(await db.FleetPlanningSettings.ToListAsync());
    Assert.Empty(await db.TruckPlanningProfiles.ToListAsync());
  }

  [Fact]
  public async Task ReadingLegacyOverridesUsesEffectiveDefaultsWithoutChangingStoredJsonOrRevision()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var original = LegacyPreferences();
    var json = JsonSerializer.Serialize(original, RoutingJson.Options);
    var updatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    db.FleetPlanningSettings.Add(
      new()
      {
        Id = SettingsId,
        SettingsJson = json,
        Revision = 9,
        UpdatedAt = updatedAt,
      }
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    using var services = new PlanningTestServices(db);

    var state = await services.Settings.GetAsync(default);
    var profile = await new TruckPlanningProfileService(
      db,
      services.Reads,
      services.Settings,
      services.ExchangeRates
    ).GetAsync(Guid.NewGuid(), default);

    AssertFuelDefaults(state.Preferences);
    AssertOtherPreferences(state.Preferences);
    AssertFuelDefaults(PlanningPreferences.From(profile));
    AssertOtherPreferences(PlanningPreferences.From(profile));
    Assert.Equal(250, profile.TankGallons);
    Assert.Equal(235.214583 / 35, profile.Mpg);
    Assert.Equal(53, profile.TrailerLengthFeet);
    var oldProfile = new TruckRouteProfile();
    original.ApplyTo(oldProfile);
    Assert.NotEqual(
      PlanningSettingsService.Signature(oldProfile),
      PlanningSettingsService.Signature(profile)
    );
    var load = new DispatchEntity { TruckId = Guid.NewGuid() };
    Assert.Equal(
      RoutePlanInputs.Hash(load, oldProfile),
      RoutePlanInputs.Hash(load, profile)
    );
    FleetFuelDefaults.Apply(original).ApplyTo(oldProfile);
    Assert.Equal(
      PlanningSettingsService.Signature(oldProfile),
      PlanningSettingsService.Signature(profile)
    );
    Assert.Equal(9, state.Revision);
    Assert.Equal(updatedAt, state.UpdatedAt);

    state.Preferences.FillPercent = 50;
    state.Preferences.UseIfta = true;
    var cachedRead = await services.Settings.GetAsync(default);
    AssertFuelDefaults(cachedRead.Preferences);
    AssertOtherPreferences(cachedRead.Preferences);
    var stored = await db.FleetPlanningSettings.AsNoTracking().SingleAsync();
    Assert.Equal(json, stored.SettingsJson);
    Assert.Equal(9, stored.Revision);
    Assert.Equal(updatedAt, stored.UpdatedAt);
    Assert.Empty(db.ChangeTracker.Entries());
  }

  [Fact]
  public async Task LegacySaveNormalizesOnlyRetiredFieldsAndPreservesConcurrencyAndInputValues()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var services = new PlanningTestServices(db);
    var requestPreferences = LegacyPreferences();

    var saved = await services.Settings.SaveAsync(
      new(requestPreferences, 0),
      default
    );
    AssertFuelDefaults(saved.Preferences);
    AssertOtherPreferences(saved.Preferences);
    Assert.Equal(90, requestPreferences.FillPercent);
    Assert.Equal(40, requestPreferences.ReserveGallons);
    Assert.Equal(120, requestPreferences.DriverHourlyCostUsd);
    Assert.Equal(27, requestPreferences.StopCostUsd);
    Assert.NotSame(requestPreferences, saved.Preferences);
    var stored = JsonSerializer.Deserialize<PlanningPreferences>(
      (await db.FleetPlanningSettings.SingleAsync()).SettingsJson,
      RoutingJson.Options
    )!;
    AssertFuelDefaults(stored);
    AssertOtherPreferences(stored);

    var unchanged = await services.Settings.SaveAsync(
      new(requestPreferences, saved.Revision),
      default
    );
    Assert.Equal(saved.Revision, unchanged.Revision);
    Assert.Equal(saved.UpdatedAt, unchanged.UpdatedAt);
    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        services.Settings.SaveAsync(new(new() { UseIfta = true }, 0), default)
    );
    AssertOtherPreferences(
      (await services.Settings.GetAsync(default)).Preferences
    );
  }

  [Theory]
  [InlineData(301, 25, 100)]
  [InlineData(35, 101, 100)]
  [InlineData(35, 25, 101)]
  public async Task InvalidLegacyInputsAreRejectedBeforeDefaultsAreApplied(
    double hourlyCost,
    double reserve,
    double fill
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var services = new PlanningTestServices(db);
    var preferences = new PlanningPreferences
    {
      DriverHourlyCostUsd = hourlyCost,
      ReserveGallons = reserve,
      FillPercent = fill,
    };
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => services.Settings.SaveAsync(new(preferences, 0), default)
    );
    Assert.Empty(await db.FleetPlanningSettings.ToListAsync());
  }

  private static PlanningPreferences LegacyPreferences() =>
    new()
    {
      UseIfta = false,
      MaxDetourMinutes = 47,
      StopCostUsd = 27,
      CadToUsd = .73,
      FillPercent = 90,
      ReserveGallons = 40,
      DriverHourlyCostUsd = 120,
    };

  private static void AssertFuelDefaults(PlanningPreferences preferences)
  {
    Assert.Equal(100, preferences.FillPercent);
    Assert.Equal(25, preferences.ReserveGallons);
    Assert.Equal(35, preferences.DriverHourlyCostUsd);
    Assert.Equal(0, preferences.StopCostUsd);
  }

  private static void AssertOtherPreferences(PlanningPreferences preferences)
  {
    Assert.False(preferences.UseIfta);
    Assert.Equal(47, preferences.MaxDetourMinutes);
    Assert.Equal(.73, preferences.CadToUsd);
  }
}
