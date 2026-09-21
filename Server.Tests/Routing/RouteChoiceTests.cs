using System.Text;
using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChoiceTests
{
  [Fact]
  public async Task ExchangeRateChangeDoesNotInvalidateUnchangedRoadPreview()
  {
    await using var fixture = await RouteChoiceFixture.CreateAsync();
    var preview = await fixture.Preview();
    var settings = await fixture.Planning.Settings.GetAsync(default);
    settings.Preferences.CadToUsd = .72;
    await fixture.Planning.Settings.SaveAsync(
      new(settings.Preferences, settings.Revision),
      default
    );
    var calls = fixture.Routing.Calls;

    var revision = await fixture.Choices.SaveAsync(
      fixture.Load.Id,
      fixture.Owner,
      new(preview.Id, 1, 0),
      default
    );

    Assert.Equal(1, revision);
    Assert.Single(await fixture.Db.DispatchRouteChoices.ToListAsync());
    Assert.Equal(calls, fixture.Routing.Calls);
  }

  [Fact]
  public async Task LongAlternativePreviewStoresGeometryOnceAndCanSaveTheExactSelectedRoad()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    var original = await f.Drafts.GetAsync(
      preview.Id,
      f.Owner,
      preview.DispatchId,
      default
    );
    var points = Enumerable
      .Range(0, 37_000)
      .Select(i => new RoutePoint(40 + 3d * i / 36_999, -80))
      .ToList();
    var route = RouteViaGeometry.Join(
      [new(2800, 150000, points)],
      [],
      f.Clock.GetUtcNow().UtcDateTime
    );
    var large = original with
    {
      Preview = preview with
      {
        Options = Enumerable
          .Range(1, 3)
          .Select(i => new RouteChoiceOption(i, route, 0, 0))
          .ToList(),
        SavedRoute = route,
      },
    };
    Assert.True(
      Encoding.UTF8.GetByteCount(
        JsonSerializer.Serialize(large, RoutingJson.Options)
      )
        > 8 * 1024 * 1024
    );

    await f.Drafts.StoreAsync(large, default);
    var stored = await f.Db.DispatchRoutePreviews.AsNoTracking().SingleAsync();
    Assert.True(Encoding.UTF8.GetByteCount(stored.DraftJson) < 8 * 1024 * 1024);
    using var json = JsonDocument.Parse(stored.DraftJson);
    foreach (
      var option in json
        .RootElement.GetProperty("preview")
        .GetProperty("options")
        .EnumerateArray()
    )
      Assert.False(option.GetProperty("route").TryGetProperty("points", out _));
    Assert.False(
      json.RootElement.GetProperty("preview")
        .GetProperty("savedRoute")
        .TryGetProperty("points", out _)
    );
    var restored = await f.Drafts.GetAsync(
      preview.Id,
      f.Owner,
      preview.DispatchId,
      default
    );
    Assert.All(
      restored.Preview.Options,
      option => Assert.Equal(points, option.Route.Legs[0].Points)
    );
    var display = RouteChoiceDisplay.Create(restored.Preview);
    Assert.All(
      display.Options,
      option => Assert.Equal(2, option.Route.Legs[0].Points.Count)
    );
    Assert.Equal(
      1,
      await f.Choices.SaveAsync(
        f.Load.Id,
        f.Owner,
        new(preview.Id, 2, 0),
        default
      )
    );
    var selected = await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync();
    var saved = JsonSerializer.Deserialize<SavedRouteChoice>(
      selected.ChoiceJson,
      RoutingJson.Options
    )!;
    Assert.Equal(points, saved.Route.Legs[0].Points);
    Assert.Equal(route.Miles, saved.Route.Miles);
    Assert.Equal(route.Seconds, saved.Route.Seconds);
  }

  [Fact]
  public async Task CompactPreviewStillRejectsGeometryOverTheByteLimitWithoutReplacingTheSavedDraft()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    var original = await f.Drafts.GetAsync(
      preview.Id,
      f.Owner,
      preview.DispatchId,
      default
    );
    var points = Enumerable
      .Range(0, 80_000)
      .Select(i => new RoutePoint(40 + 3d * i / 79_999, -80))
      .ToList();
    var route = RouteViaGeometry.Join(
      [new(2800, 150000, points)],
      [],
      f.Clock.GetUtcNow().UtcDateTime
    );
    var oversized = original with
    {
      Preview = preview with
      {
        Id = Guid.NewGuid(),
        Options = Enumerable
          .Range(1, 3)
          .Select(i => new RouteChoiceOption(i, route, 0, 0))
          .ToList(),
      },
    };
    Assert.True(
      Encoding.UTF8.GetByteCount(RoutePlanStorage.Serialize(oversized))
        > 8 * 1024 * 1024
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Drafts.StoreAsync(oversized, default)
    );
    Assert.Equal(
      preview.Id,
      (
        await f.Drafts.GetAsync(
          preview.Id,
          f.Owner,
          preview.DispatchId,
          default
        )
      )
        .Preview
        .Id
    );
  }

  [Fact]
  public async Task PreviewIsOwnerBoundSharedBetweenServicesExpiresAndRetainsOnlyLatest()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    preview.Options[0].Route.Miles = 999;
    var otherService = new RouteChoiceDrafts(f.Db, f.Clock);
    var read = await otherService.GetAsync(
      preview.Id,
      f.Owner,
      preview.DispatchId,
      default
    );
    Assert.Equal(100, read.Preview.Options[0].Route.Miles);
    read.Preview.Options.Clear();
    Assert.Equal(
      2,
      (
        await f.Drafts.GetAsync(
          preview.Id,
          f.Owner,
          preview.DispatchId,
          default
        )
      )
        .Preview
        .Options
        .Count
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Drafts.GetAsync(
          preview.Id,
          Guid.NewGuid(),
          preview.DispatchId,
          default
        )
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Drafts.GetAsync(preview.Id, f.Owner, Guid.NewGuid(), default)
    );
    var next = await f.Preview();
    Assert.Single(await f.Db.DispatchRoutePreviews.ToListAsync());
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Drafts.GetAsync(preview.Id, f.Owner, preview.DispatchId, default)
    );
    f.Db.ChangeTracker.Clear();
    f.Clock.Advance(TimeSpan.FromMinutes(10));
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Drafts.GetAsync(next.Id, f.Owner, preview.DispatchId, default)
    );
    await f.Preview();
    Assert.Single(await f.Db.DispatchRoutePreviews.ToListAsync());
  }

  [Fact]
  public async Task PreviewDoesNotWriteAndSavedChoiceSurvivesRefreshAndCompletion()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    Assert.Equal(2, preview.Options.Count);
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
    Assert.Empty(await f.Db.DispatchBaseRoutes.ToListAsync());
    var originalHash = RoutePlanInputs.Hash(
      f.Load,
      await f.Planning.Routes.ProfileAsync(f.Truck.Id, default)
    );
    Assert.Equal(
      1,
      await f.Choices.SaveAsync(
        f.Load.Id,
        f.Owner,
        new(preview.Id, 2, 0),
        default
      )
    );
    var row = await f.Db.DispatchRouteChoices.SingleAsync();
    Assert.Equal(f.Owner, row.RecordedBy);
    Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, row.RecordedAt);
    var load = await f.Planning.Routes.LoadAsync(f.Load.Id, default);
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);
    Assert.NotEqual(originalHash, RoutePlanInputs.Hash(load, profile));
    var calls = f.Routing.Calls;
    var road = await f.Planning.BaseRoutes.EnsureAsync(load, profile, default);
    Assert.Equal(110, road.Miles);
    Assert.Equal(preview.Options[1].Route.CalculatedAt, road.CalculatedAt);
    Assert.Equal(calls, f.Routing.Calls);
    load = load with
    {
      Stops = load.Stops.SetItem(
        0,
        load.Stops[0] with
        {
          ManualCompletedAt = DateTime.UtcNow,
          ManualCompletionRevision = load.Stops[0].ManualCompletionRevision + 1,
        }
      ),
    };
    Assert.Equal(
      110,
      (await f.Planning.BaseRoutes.EnsureAsync(load, profile, default)).Miles
    );
    var saved = JsonSerializer.Deserialize<SavedRouteChoice>(
      row.ChoiceJson,
      RoutingJson.Options
    )!;
    Assert.Equal(2, saved.Stops.Count);
    Assert.Equal(
      preview.Options[1].Route.Legs[0].Points,
      saved.Route.Legs[0].Points
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(f.Load.Id, f.Owner, new(preview.Id, 1, 0), default)
    );
  }

  [Theory]
  [InlineData("destination")]
  [InlineData("truck")]
  [InlineData("completed")]
  [InlineData("revision")]
  public async Task StaleDraftCannotOverwriteChangedLoad(string change)
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    var changed = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    switch (change)
    {
      case "destination":
        changed.Stops.OrderBy(x => x.Sequence).Last().Latitude = 44;
        break;
      case "truck":
        changed.TruckId = null;
        break;
      case "completed":
        changed.Status = "completed";
        break;
      default:
        changed.RouteChoiceRevision = 7;
        break;
    }
    await f.Db.SaveChangesAsync();
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(f.Load.Id, f.Owner, new(preview.Id, 1, 0), default)
    );
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }

  [Fact]
  public async Task ChangedRoadInputsRequireReviewInsteadOfSilentlyDiscardingChoice()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 2, 0),
      default
    );
    var load = await f.Planning.Routes.LoadAsync(f.Load.Id, default);
    load = load with
    {
      Stops = load.Stops.SetItem(1, load.Stops[1] with { Longitude = -78 }),
    };
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);
    var calls = f.Routing.Calls;
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Planning.BaseRoutes.EnsureAsync(load, profile, default)
    );
    Assert.Equal(calls, f.Routing.Calls);
  }

  [Fact]
  public async Task CurrentGpsReconnectsWithoutReplacingTheSelectedSuffix()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 2, 0),
      default
    );
    var load = await f.Planning.Routes.LoadAsync(f.Load.Id, default);
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);
    var selected = preview.Options[1].Route.Legs[0].Points;
    var calls = f.Routing.Calls;
    var route = await f.Planning.BaseRoutes.CurrentAsync(
      load,
      profile,
      selected[1],
      [preview.Stops[1]],
      default
    );
    Assert.Single(route.Legs);
    Assert.Equal(selected[^1], route.Legs[0].Points[^1]);
    Assert.Equal(calls, f.Routing.Calls);
    Assert.InRange(route.Miles, 0, 110);
  }

  [Fact]
  public async Task ProviderCannotSkipTheRequestedViaPoint()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    f.Routing.MissVia = true;
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.PreviewAsync(
          f.Load.Id,
          f.Owner,
          new([new(Guid.NewGuid(), f.Load.Stops[1].Id, "Via", new(41, -80))]),
          default
        )
    );
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }
}
