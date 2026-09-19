using System.Reflection;
using API.Controllers;
using Application.Caching;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public class PlanningSettingsTests
{
  [Fact]
  public async Task PreferencesPersistAcrossContextsAndUnchangedSaveKeepsTheRevision()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    await using (var db = new AppDbContext(options))
    {
      await db.Database.EnsureCreatedAsync();
      var service = new PlanningSettingsService(db, cache);
      var defaults = await service.GetAsync(default);
      Assert.True(defaults.Preferences.UseIfta);
      Assert.Equal(0, defaults.Revision);
      var preferences = new PlanningPreferences
      {
        UseIfta = false,
        MaxDetourMinutes = 8,
        FillPercent = 95,
        ReserveGallons = 30,
        CadToUsd = .73,
      };
      var saved = await service.SaveAsync(
        new(preferences, defaults.Revision),
        default
      );
      Assert.Equal(1, saved.Revision);
      var repeated = await service.SaveAsync(
        new(preferences, saved.Revision),
        default
      );
      Assert.Equal(saved.Revision, repeated.Revision);
      Assert.Equal(saved.UpdatedAt, repeated.UpdatedAt);
    }
    await using var reloaded = new AppDbContext(options);
    cache.Invalidate("settings");
    var result = await new PlanningSettingsService(reloaded, cache).GetAsync(
      default
    );
    Assert.False(result.Preferences.UseIfta);
    Assert.Equal(8, result.Preferences.MaxDetourMinutes);
    Assert.Equal(100, result.Preferences.FillPercent);
    Assert.Equal(25, result.Preferences.ReserveGallons);
    Assert.Equal(.73, result.Preferences.CadToUsd);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(61)]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  public async Task InvalidSettingsDoNotReachStorage(double detour)
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var services = new PlanningTestServices(db);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        services.Settings.SaveAsync(
          new(new() { MaxDetourMinutes = detour }, 0),
          default
        )
    );
    Assert.Empty(await db.FleetPlanningSettings.ToListAsync());
  }

  [Fact]
  public async Task StaleSettingsCannotOverwriteAnotherSessionsChanges()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var first = new AppDbContext(options);
    await first.Database.EnsureCreatedAsync();
    await using var second = new AppDbContext(options);
    using var services = new PlanningTestServices(first);
    var otherSession = new PlanningSettingsService(second, services.Reads);
    var stale = await otherSession.GetAsync(default);
    await services.Settings.SaveAsync(
      new(new() { UseIfta = false }, 0),
      default
    );
    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        otherSession.SaveAsync(new(stale.Preferences, stale.Revision), default)
    );
    Assert.False((await otherSession.GetAsync(default)).Preferences.UseIfta);
  }

  [Fact]
  public void SharedPreferencesPreserveVehicleAndTelemetryParameters()
  {
    var profile = new TruckRouteProfile
    {
      TankGallons = 200,
      Mpg = 7.5,
      HeightFeet = 13.5,
      LengthFeet = 72,
    };
    new PlanningPreferences { UseIfta = false, MaxDetourMinutes = 5 }.ApplyTo(
      profile
    );
    Assert.False(profile.UseIfta);
    Assert.Equal(5, profile.MaxDetourMinutes);
    Assert.Equal(200, profile.TankGallons);
    Assert.Equal(7.5, profile.Mpg);
    Assert.Equal(72, profile.LengthFeet);
    Assert.Equal(53, profile.TrailerLengthFeet);
  }

  [Fact]
  public void FleetSettingsRequireAuthentication()
  {
    Assert.NotNull(
      typeof(SettingsController).GetCustomAttribute<AuthorizeAttribute>()
    );
    Assert.Null(
      typeof(SettingsController)
        .GetMethod(nameof(SettingsController.Save))!
        .GetCustomAttribute<AllowAnonymousAttribute>()
    );
  }
}
