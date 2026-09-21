using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChoiceSettingsPublicationTests
{
  [Theory]
  [InlineData(false, "before-preview")]
  [InlineData(true, "before-preview")]
  [InlineData(false, "provider")]
  [InlineData(true, "provider")]
  [InlineData(false, "preview-publication")]
  [InlineData(true, "preview-publication")]
  [InlineData(false, "before-save")]
  [InlineData(true, "before-save")]
  [InlineData(false, "save-publication")]
  [InlineData(true, "save-publication")]
  public async Task ChangedDimensionsKeepThePreviousDraftAndRoads(
    bool current,
    string phase
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    if (current)
      await f.StartTripAsync();
    var first = await f.Preview();
    await f.Choices.SaveAsync(f.Load.Id, f.Owner, new(first.Id, 1, 0), default);
    f.Db.ChangeTracker.Clear();
    var preview = await f.Preview();
    var before = await SnapshotAsync(f);
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    var generation = f.Planning.Reads.Generation($"profile:{f.Truck.Id}");
    var calls = f.Routing.Calls;
    string? changed = null;
    async Task Change()
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      profile.HeightFeet = 14;
      changed = await PlanningProfileFixture.SaveAsync(
        f.Db,
        f.Truck.Id,
        profile
      );
    }
    if (phase.StartsWith("before-", StringComparison.Ordinal))
      await Change();
    else if (phase == "provider")
      f.Routing.BeforeCalculate = Change;
    else
      f.Publication.BeforeBegin = Change;

    var error = await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (phase.Contains("save", StringComparison.Ordinal))
        await f.Choices.SaveAsync(
          f.Load.Id,
          f.Owner,
          new(preview.Id, 2, preview.Revision),
          default
        );
      else
        await f.Preview();
    });

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
    if (
      phase.StartsWith("before-", StringComparison.Ordinal)
      || phase.Contains("save", StringComparison.Ordinal)
    )
      Assert.Equal(calls, f.Routing.Calls);
    else
      Assert.True(f.Routing.Calls > calls);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [InlineData(true, true)]
  public async Task FuelOnlyChangesDoNotRejectRoadChoices(
    bool current,
    bool change
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    if (current)
      await f.StartTripAsync();
    await f.Planning.Settings.SaveAsync(new(new(), 0), default);
    var preferences = new PlanningPreferences
    {
      UseIfta = false,
      CadToUsd = .7,
    };
    if (change)
      f.Publication.BeforeBegin = async () =>
      {
        var json = JsonSerializer.Serialize(preferences, RoutingJson.Options);
        await f.Db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, json)
        );
      };
    var preview = await f.Preview();
    var calls = f.Routing.Calls;

    var revision = await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 2, 0),
      default
    );

    Assert.Equal(1, revision);
    Assert.Equal(calls, f.Routing.Calls);
    Assert.Single(await f.Db.DispatchRouteChoices.AsNoTracking().ToListAsync());
    Assert.Single(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task NativeChoiceRejectsSettingsWithoutAdvancingLegRevision()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var leg = await f.AddExecutionAsync("planned");
    var preview = await f.Choices.PreviewAsync(
      f.Load.Id,
      f.Owner,
      new([], ExecutionLegId: leg.Id),
      default
    );
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    f.Publication.BeforeBegin = async () =>
    {
      profile.Hazmat = "USHazmatClass3";
      await PlanningProfileFixture.SaveAsync(f.Db, f.Truck.Id, profile);
    };

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(
          f.Load.Id,
          f.Owner,
          new(preview.Id, 1, 0, leg.Id),
          default
        )
    );

    Assert.Equal(
      0,
      await f.Db.ExecutionLegs.Select(x => x.RouteChoiceRevision).SingleAsync()
    );
    Assert.Empty(await f.Db.DispatchRouteChoices.AsNoTracking().ToListAsync());
    Assert.Empty(await f.Db.DispatchBaseRoutes.AsNoTracking().ToListAsync());
    Assert.Equal(
      preview.Id,
      await f.Db.DispatchRoutePreviews.Select(x => x.PreviewId).SingleAsync()
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static async Task<string> SnapshotAsync(RouteChoiceFixture f) =>
    JsonSerializer.Serialize(
      new
      {
        Drafts = await f.Db.DispatchRoutePreviews.AsNoTracking().ToArrayAsync(),
        Choices = await f.Db.DispatchRouteChoices.AsNoTracking().ToArrayAsync(),
        Bases = await f.Db.DispatchBaseRoutes.AsNoTracking().ToArrayAsync(),
        Plans = await f.Db.DispatchRoutePlans.AsNoTracking().ToArrayAsync(),
        Revisions = await f
          .Db.Dispatches.Select(x => x.RouteChoiceRevision)
          .ToArrayAsync(),
      }
    );
}
