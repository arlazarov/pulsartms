using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task LegacyFuelCannotWriteToAnotherTrucksRouteRow()
  {
    await using var f = await CreateFuelEditingFixtureAsync();
    var other = new Truck { Id = Guid.NewGuid(), ExternalId = "other" };
    f.Db.Trucks.Add(other);
    await f.Db.SaveChangesAsync();
    await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.TruckId, other.Id)
    );
    f.Db.ChangeTracker.Clear();
    var before = await FuelRowsAsync(f);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var beforeProfile = await SavedProfileJsonAsync(f);
    profile.Confirmed = !profile.Confirmed;

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Fuel.BuildAsync(f.Load.Id, new(profile), default)
    );

    Assert.Equal(before, await FuelRowsAsync(f));
    Assert.Equal(beforeProfile, await SavedProfileJsonAsync(f));
    Assert.Equal(
      other.Id,
      await f.Db.DispatchRoutePlans.Select(x => x.TruckId).SingleAsync()
    );
  }

  [Fact]
  public async Task ProfileSaveRejectsWorkChangedAtThePublicationBoundary()
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    profile.HeightFeet = 14;
    f.Publication.BeforeBegin = async () =>
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "changed"));

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Plans.SaveProfileAsync(f.Load.Id, profile, default)
    );

    Assert.Empty(await f.Db.TruckPlanningProfiles.AsNoTracking().ToListAsync());
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ProfileSaveOnlyInvalidatesTheCacheAfterCommit(bool fail)
  {
    var failure = new PublicationCommitFailureProbe();
    await using var f = await Fixture.CreateAsync(publicationFailure: failure);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var generation = f.Services.Reads.Generation($"profile:{f.Truck.Id}");
    profile.HeightFeet = 14;
    f.Publication.BeforeBegin = () =>
    {
      Assert.Equal(
        generation,
        f.Services.Reads.Generation($"profile:{f.Truck.Id}")
      );
      failure.FailNextCommit = fail;
      return Task.CompletedTask;
    };

    if (fail)
      await Assert.ThrowsAsync<InvalidOperationException>(
        () => f.Plans.SaveProfileAsync(f.Load.Id, profile, default)
      );
    else
      await f.Plans.SaveProfileAsync(f.Load.Id, profile, default);

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      fail ? 13.5 : 14,
      (await f.Plans.ProfileAsync(f.Truck.Id, default)).HeightFeet
    );
    Assert.Equal(fail ? 0 : 1, await f.Db.TruckPlanningProfiles.CountAsync());
    Assert.Equal(
      fail,
      generation == f.Services.Reads.Generation($"profile:{f.Truck.Id}")
    );
  }

  [Theory]
  [InlineData("completed")]
  [InlineData("revision")]
  public async Task ProfileSaveRejectsAnObsoleteWorkReference(string changed)
  {
    await using var f = await Fixture.CreateAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    if (changed == "completed")
    {
      f.Load.Status = "completed";
      await f.Db.SaveChangesAsync();
    }

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Plans.SaveProfileAsync(
          f.Load.Id,
          profile,
          default,
          assignmentRevision: changed == "revision" ? 1 : null
        )
    );

    Assert.Equal(0, f.Publication.Calls);
    Assert.Empty(await f.Db.TruckPlanningProfiles.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData("prices")]
  [InlineData("work")]
  [InlineData("snapshot")]
  [InlineData("commit")]
  public async Task FailedFuelCalculationPreservesProfileAndBothSavedCopies(
    string stage
  )
  {
    var writes = new FuelCommitFailureProbe();
    var commit = new PublicationCommitFailureProbe();
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      failure: writes,
      publicationFailure: commit
    );
    f.Location.FuelPercent = 40;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    var previous = await FuelRowsAsync(f);
    var beforeProfile = await SavedProfileJsonAsync(f);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    profile.Confirmed = !profile.Confirmed;
    var generation = f.Services.Reads.Generation($"profile:{f.Truck.Id}");
    f.Sender.BeforeFuel = async _ =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      Assert.Equal(beforeProfile, await SavedProfileJsonAsync(f));
      if (stage == "prices")
        throw new InvalidOperationException("Prices unavailable.");
    };
    f.Publication.BeforeBegin = async () =>
    {
      if (stage == "work")
        await f
          .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "changed"));
      writes.FailNextSnapshotWrite = stage == "snapshot";
      commit.FailNextCommit = stage == "commit";
    };

    var error = await Record.ExceptionAsync(
      () => f.Services.Fuel.BuildAsync(f.Load.Id, new(profile), default)
    );

    Assert.NotNull(error);
    if (stage == "work")
      Assert.IsType<RoutePlanningException>(error);
    else
      Assert.IsType<InvalidOperationException>(error);
    Assert.Equal(beforeProfile, await SavedProfileJsonAsync(f));
    Assert.Equal(previous, await FuelRowsAsync(f));
    Assert.Equal(
      generation,
      f.Services.Reads.Generation($"profile:{f.Truck.Id}")
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    if (stage == "snapshot")
      Assert.Equal(1, writes.RouteWritesBeforeFailure);
  }

  [Fact]
  public async Task FuelSuccessCommitsTheRequestedProfileWithBothCopies()
  {
    await using var f = await CreateFuelEditingFixtureAsync();
    var before = await SavedProfileJsonAsync(f);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    profile.Confirmed = !profile.Confirmed;

    var fuel = await f.Services.Fuel.BuildAsync(
      f.Load.Id,
      new(profile),
      default
    );

    Assert.NotEqual(before, await SavedProfileJsonAsync(f));
    var effective = await f.Plans.ProfileAsync(f.Truck.Id, default);
    Assert.Equal(profile.Confirmed, effective.Confirmed);
    Assert.Equal(
      JsonSerializer.Serialize(effective, RoutingJson.Options),
      fuel.Plan!.ProfileSignature
    );
    var route = await f.Plans.GetAsync(f.Load.Id, default);
    Assert.Equal(
      fuel.Plan!.ProfileSignature,
      route.Plan!.FuelPlan!.ProfileSignature
    );
    var saved = await f.Services.FuelPlans.ReadUncachedAsync(
      f.Truck.Id,
      default
    );
    Assert.Equal(fuel.Plan!.ProfileSignature, saved!.Plan.ProfileSignature);
    Assert.Equal(fuel.Plan!.CalculatedAt, saved.Plan!.CalculatedAt);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task FuelPublicationBypassesWarmProfileAndSettingsCaches(
    bool manual,
    bool settings
  )
  {
    await using var f = await CreateFuelEditingFixtureAsync();
    var preview = await f.Services.Fuel.EditAsync(
      f.Load.Id,
      new(null, null),
      false,
      default
    );
    var previous = await FuelRowsAsync(f);
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var preferences = await f.Services.Settings.GetAsync(default);
    await f.Services.Settings.SaveAsync(
      new(preferences.Preferences, preferences.Revision),
      default
    );
    await f.Services.Settings.GetAsync(default);
    string? changedProfile = null;
    f.Publication.BeforeBegin = async () =>
    {
      if (settings)
      {
        var changed = PlanningPreferences.From(profile);
        changed.UseIfta = !changed.UseIfta;
        var json = JsonSerializer.Serialize(changed, RoutingJson.Options);
        await f.Db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, json)
        );
      }
      else
      {
        profile.HeightFeet = 14;
        changedProfile = JsonSerializer.Serialize(profile, RoutingJson.Options);
        await f.Db.TruckPlanningProfiles.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, changedProfile)
        );
      }
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(async () =>
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

    Assert.Contains("Truck planning settings changed", error.Message);
    Assert.Equal(previous, await FuelRowsAsync(f));
    if (!settings)
      Assert.Equal(changedProfile, await SavedProfileJsonAsync(f));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static Task<string> SavedProfileJsonAsync(Fixture f) =>
    f
      .Db.TruckPlanningProfiles.AsNoTracking()
      .Where(x => x.TruckId == f.Truck.Id)
      .Select(x => x.SettingsJson)
      .SingleAsync();
}
