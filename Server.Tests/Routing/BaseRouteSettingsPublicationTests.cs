using System.Text.Json;
using Application.Features.Execution.Services;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRouteSettingsPublicationTests
{
  [Theory]
  [InlineData(false, "before")]
  [InlineData(true, "before")]
  [InlineData(false, "provider")]
  [InlineData(true, "provider")]
  [InlineData(false, "publication")]
  [InlineData(true, "publication")]
  public async Task StaleDimensionsCannotReplaceAnAssignedOrHistoricalBase(
    bool historical,
    string phase
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var load = RouteWorkProjection.Capture(f.Load);
    if (historical)
    {
      var leg = await f.AddExecutionAsync("completed");
      load = RouteWorkProjection.Capture(f.Load, leg, f.Load.Stops);
    }
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    await f.Planning.BaseRoutes.EnsureAsync(load, profile, default);
    await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.InputHash, "previous road")
    );
    var before = await SnapshotAsync(f);
    var calls = f.Routing.Calls;
    var generation = f.Planning.Reads.Generation($"profile:{f.Truck.Id}");
    string? changed = null;
    async Task Change()
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      var next = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
      next.HeightFeet = 14;
      changed = await PlanningProfileFixture.SaveAsync(f.Db, f.Truck.Id, next);
    }
    if (phase == "before")
      await Change();
    else if (phase == "provider")
      f.Routing.BeforeCalculate = Change;
    else
      f.Publication.BeforeBegin = Change;

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Planning.BaseRoutes.EnsureAsync(load, profile, default)
    );

    Assert.Contains("Truck routing settings changed", error.Message);
    Assert.Equal(before, await SnapshotAsync(f));
    Assert.Equal(
      changed,
      await f.Db.TruckPlanningProfiles.Select(x => x.SettingsJson).SingleAsync()
    );
    Assert.Equal(
      generation,
      f.Planning.Reads.Generation($"profile:{f.Truck.Id}")
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Empty(f.Db.DispatchBaseRoutes.Local);
    Assert.Equal(calls + (phase == "before" ? 0 : 1), f.Routing.Calls);
    if (historical)
      Assert.Equal(
        "completed",
        await f.Db.ExecutionLegs.Select(x => x.Status).SingleAsync()
      );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelOnlyChangesStillAllowBasePublication(bool unassigned)
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    if (unassigned)
    {
      f.Load.TruckId = null;
      f.Load.Status = "unassigned";
      await f.Db.Dispatches.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.TruckId, (Guid?)null)
          .SetProperty(x => x.Status, "unassigned")
      );
    }
    await f.Planning.Settings.SaveAsync(new(new(), 0), default);
    var profile = await f.Planning.Profiles.GetAsync(
      unassigned ? Guid.Empty : f.Truck.Id,
      default
    );
    f.Routing.BeforeCalculate = () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      return Task.CompletedTask;
    };
    f.Publication.BeforeBegin = async () =>
    {
      var preferences = new PlanningPreferences
      {
        UseIfta = false,
        CadToUsd = .7,
      };
      var json = JsonSerializer.Serialize(
        preferences,
        RoutePlanningService.Json
      );
      await f.Db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.SettingsJson, json)
      );
      if (unassigned)
        await PlanningProfileFixture.SaveAsync(
          f.Db,
          f.Truck.Id,
          new() { UsesFleetDefaults = true, HeightFeet = 14 }
        );
    };

    var route = await f.Planning.BaseRoutes.EnsureAsync(
      f.Load,
      profile,
      default
    );

    Assert.Single(route.Legs);
    Assert.Equal(1, f.Publication.Calls);
    Assert.Equal(1, f.Routing.Calls);
    Assert.Single(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
    Assert.Empty(f.Db.DispatchBaseRoutes.Local);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task StandalonePublicationDoesNotJoinACallerTransaction()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    await using var transaction = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Planning.BaseRoutes.EnsureAsync(f.Load, profile, default)
    );

    Assert.Same(transaction, f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Routing.Calls);
    Assert.Empty(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ManualBuildKeepsRequestedAndObservedDimensionsSeparate(
    bool conflict
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var requested = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    requested.HeightFeet = 14;
    if (conflict)
      f.Publication.BeforeBegin = async () =>
        await PlanningProfileFixture.SaveAsync(
          f.Db,
          f.Truck.Id,
          new() { UsesFleetDefaults = true, HeightFeet = 15 }
        );

    if (conflict)
    {
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => f.Planning.Routes.BuildAsync(f.Load.Id, new(requested), default)
      );
      Assert.Empty(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
      Assert.Empty(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
      Assert.Equal(
        15,
        (
          await f.Planning.Profiles.GetUncachedAsync(f.Truck.Id, default)
        ).HeightFeet
      );
    }
    else
    {
      var plan = await f.Planning.Routes.BuildAsync(
        f.Load.Id,
        new(requested),
        default
      );
      Assert.Equal(14, plan.Profile.HeightFeet);
      Assert.Equal(
        14,
        (
          await f.Planning.Profiles.GetUncachedAsync(f.Truck.Id, default)
        ).HeightFeet
      );
      Assert.Single(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
      Assert.Single(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
    }
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FailedCommitKeepsTheBaseAndNativePlanningRequests(
    bool historical
  )
  {
    var commits = new PublicationCommitFailureProbe();
    await using var f = await RouteChoiceFixture.CreateAsync(commits);
    var load = RouteWorkProjection.Capture(f.Load);
    if (historical)
    {
      var leg = await f.AddExecutionAsync("completed");
      load = RouteWorkProjection.Capture(f.Load, leg, f.Load.Stops);
    }
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    await f.Planning.BaseRoutes.EnsureAsync(load, profile, default);
    await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.InputHash, "previous road")
    );
    var before = await SnapshotAsync(f);
    var requests = await f.Db.ExecutionPlanningChanges.CountAsync();
    f.Publication.BeforeBegin = () =>
    {
      commits.FailNextCommit = true;
      return Task.CompletedTask;
    };

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Planning.BaseRoutes.EnsureAsync(load, profile, default)
    );

    Assert.Equal("Publication commit failed.", error.Message);
    Assert.Equal(before, await SnapshotAsync(f));
    Assert.Equal(requests, await f.Db.ExecutionPlanningChanges.CountAsync());
    Assert.Empty(f.Db.DispatchBaseRoutes.Local);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static async Task<string> SnapshotAsync(RouteChoiceFixture f) =>
    JsonSerializer.Serialize(
      await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()
    );
}
