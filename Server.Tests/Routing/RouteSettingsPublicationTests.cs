using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task GpsBuildRetainsTheSavedFullRouteForDisplay()
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var full = await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    f.Db.DispatchRoutePlans.Remove(await f.Db.DispatchRoutePlans.SingleAsync());
    await f.Db.SaveChangesAsync();
    var current = await f.Plans.BuildAsync(
      f.Load.Id,
      new(profile, true, 1),
      default
    );
    Assert.True(current.FromCurrentPosition);
    Assert.NotNull(current.ReferenceRoute);
    Assert.Equal(full.Route.Miles, current.ReferenceRoute.Miles);
    var stored = RoutePlanStorage.Read(
      (
        await RoutePlanStorage.LoadAsync(
          f.Db,
          await f.Db.DispatchRoutePlans.SingleAsync(),
          default
        )
      )!
    )!;
    Assert.NotNull(stored.ReferenceRoute);
    Assert.Equal(
      full.Route.Legs[0].Points,
      stored.ReferenceRoute.Legs[0].Points
    );
  }

  [Fact]
  public async Task OffRouteGpsBuildReconnectsPickupWithoutChangingRemainingMiles()
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    f.Db.DispatchRoutePlans.Remove(await f.Db.DispatchRoutePlans.SingleAsync());
    await f.Db.SaveChangesAsync();
    f.Location.Latitude = 41;
    var current = await f.Plans.BuildAsync(
      f.Load.Id,
      new(profile, true, 2),
      default
    );
    Assert.NotNull(current.ReferenceStops);
    var reference = Assert.Single(current.ReferenceRoute!.Legs);
    var remaining = Assert.Single(current.Route.Legs);
    Assert.Equal(new RoutePoint(40, -80), reference.Points[0]);
    Assert.Contains(remaining.Points[0], reference.Points);
    Assert.Equal(remaining.Points[^1], reference.Points[^1]);
    Assert.True(reference.Miles > remaining.Miles);
    var stored = RoutePlanStorage.Read(
      (
        await RoutePlanStorage.LoadAsync(
          f.Db,
          await f.Db.DispatchRoutePlans.SingleAsync(),
          default
        )
      )!
    )!;
    Assert.Equal(reference.Points, stored.ReferenceRoute!.Legs[0].Points);
    Assert.Equal(remaining.Miles, stored.Route.Miles);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task RouteBuildRejectsSettingsChangedAtPublication(
    bool automatic,
    bool fleet
  )
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    var before = (
      await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
    ).PlanJson;
    await f.Services.Settings.SaveAsync(new(new(), 0), default);
    var changed = await f.Plans.ProfileAsync(f.Truck.Id, default);
    string? changedJson = null;
    f.Publication.BeforeBegin = async () =>
    {
      if (fleet)
      {
        var preferences = PlanningPreferences.From(changed);
        preferences.UseIfta = !preferences.UseIfta;
        var json = JsonSerializer.Serialize(preferences, RoutingJson.Options);
        await f.Db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, json)
        );
      }
      else
      {
        changed.HeightFeet = 14;
        changedJson = await PlanningProfileFixture.SaveAsync(
          f.Db,
          f.Truck.Id,
          changed
        );
      }
    };
    before = OldRoute(before);
    await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.PlanJson, before)
    );
    f.Db.ChangeTracker.Clear();
    f.Location.Longitude = -81;
    var calls = f.Router.Calls;

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Plans.BuildAsync(
          f.Load.Id,
          new(profile, true, 1),
          default,
          automatic: automatic
        )
    );

    Assert.Contains("Truck planning settings changed", error.Message);
    Assert.True(f.Router.Calls > calls);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
    if (!fleet)
      Assert.Equal(changedJson, await SavedProfileJsonAsync(f));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task AutomaticRouteRejectsACachedOldProfileBeforeProviderWork()
  {
    await using var f = await Fixture.CreateAsync();
    var cached = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var changed = await f.Plans.ProfileAsync(f.Truck.Id, default);
    changed.HeightFeet = 14;
    var json = await PlanningProfileFixture.SaveAsync(
      f.Db,
      f.Truck.Id,
      changed
    );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Plans.BuildAsync(
          f.Load.Id,
          new(cached, true, 1),
          default,
          automatic: true
        )
    );

    Assert.Equal(0, f.Router.Calls);
    Assert.Equal(json, await SavedProfileJsonAsync(f));
    Assert.Empty(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task ManualRouteCanCommitItsRequestedDimensionChange()
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    profile.HeightFeet = 14;

    var plan = await f.Plans.BuildAsync(
      f.Load.Id,
      new(profile, true, 1),
      default
    );

    Assert.Equal(14, plan.Profile.HeightFeet);
    Assert.Equal(
      14,
      (await f.Plans.ProfileAsync(f.Truck.Id, default)).HeightFeet
    );
    Assert.Single(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RouteProgressRejectsLateDimensionChanges(bool reroute)
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    await f.Plans.BuildAsync(f.Load.Id, new(profile), default);
    var before = (
      await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
    ).PlanJson;
    f.Location.Longitude = reroute ? -81 : -80;
    f.Publication.BeforeBegin = async () =>
    {
      profile.HeightFeet = 14;
      await PlanningProfileFixture.SaveAsync(f.Db, f.Truck.Id, profile);
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Plans.AdvanceAutomaticallyAsync(
          f.Load.Id,
          default,
          forceReroute: reroute
        )
    );

    Assert.Contains("Truck routing settings changed", error.Message);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static string OldRoute(string json)
  {
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      json,
      RoutingJson.Options
    )!;
    plan.CalculatedAt = DateTime.UtcNow.AddMinutes(-10);
    return JsonSerializer.Serialize(plan, RoutingJson.Options);
  }
}
