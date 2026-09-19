using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class CurrentRouteChoiceTests
{
  [Theory]
  [InlineData("planned", false)]
  [InlineData("planned", true)]
  [InlineData("active", false)]
  public async Task ReviewedCurrentAssignmentOptionsStartAtGps(
    string status,
    bool cold
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var leg = await f.AddExecutionAsync(status);
    await f
      .Db.ExecutionLegs.Where(x => x.Id == leg.Id)
      .ExecuteUpdateAsync(setters =>
        setters.SetProperty(
          x => x.SourceReviewReason,
          "Trailer conflicts with another assignment."
        )
      );
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);
    if (!cold)
      await f.Planning.Routes.BuildAsync(
        f.Load.Id,
        new(profile) { ExecutionLegId = leg.Id },
        default
      );
    f.Location = new()
    {
      TruckId = f.Truck.Id,
      Latitude = 41.5m,
      Longitude = -78m,
      UpdatedAt = f.Clock.GetUtcNow().UtcDateTime,
    };
    f.Routing.OptionCount = 2;

    var preview = await f.Choices.PreviewAsync(
      f.Load.Id,
      f.Owner,
      new([], true) { ExecutionLegId = leg.Id },
      default
    );

    Assert.Equal(2, preview.Options.Count);
    Assert.Equal(new RoutePoint(41.5, -78), preview.Stops[0].Point);
    Assert.Equal(preview.Stops[0].Point, f.Routing.LastPoints[0]);
    Assert.Equal(f.Location.UpdatedAt, preview.OriginUpdatedAt);
    await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 1, 0, leg.Id),
      default
    );
    var plan = SavedRouteReader.Plan(
      (await f.Db.DispatchRoutePlans.SingleAsync()).PlanJson
    )!;
    Assert.True(plan.FromCurrentPosition);
    Assert.Equal(preview.Stops[0].Point, plan.Route.Legs[0].Points[0]);
    Assert.Equal(status, (await f.Db.ExecutionLegs.SingleAsync()).Status);
  }

  [Fact]
  public async Task FutureLoadDoesNotUseTheTruckGpsOrDiscardItsPickup()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    var future = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = f.Truck.Id,
      Status = "assigned",
      LoadNumber = 1400,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 44,
          Longitude = -80,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Latitude = 46,
          Longitude = -80,
          Job = "Drop Off",
        },
      ],
    };
    f.Db.Dispatches.Add(future);
    await f.Db.SaveChangesAsync();
    f.Planning.Reads.Invalidate("board");
    var preview = await f.Choices.PreviewAsync(
      future.Id,
      f.Owner,
      new([]),
      default
    );
    Assert.Null(preview.OriginUpdatedAt);
    Assert.Equal(future.Stops[0].Id, preview.Stops[0].Id);
    Assert.Equal(new RoutePoint(44, -80), f.Routing.LastPoints[0]);
  }

  [Fact]
  public async Task ReopeningKeepsChosenRoadAndDoesNotReturnToPassedViaPoints()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    var passedVia = new RouteViaPoint(
      Guid.NewGuid(),
      f.Load.Stops[1].Id,
      "Earlier via",
      new(41.7, -80)
    );
    var aheadVia = new RouteViaPoint(
      Guid.NewGuid(),
      f.Load.Stops[1].Id,
      "Later via",
      new(42.8, -80)
    );
    var preview = await f.Choices.PreviewAsync(
      f.Load.Id,
      f.Owner,
      new([passedVia, aheadVia], false),
      default
    );
    await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 1, 0),
      default
    );
    f.Location!.Latitude = 42;
    var next = await f.Choices.PreviewAsync(
      f.Load.Id,
      f.Owner,
      new([], true, true),
      default
    );
    Assert.Equal(new RoutePoint(42, -80), next.Stops[0].Point);
    Assert.Equal(aheadVia.Id, Assert.Single(next.ViaPoints).Id);
    Assert.NotNull(next.SavedRoute);
    Assert.Equal(1, next.Revision);
  }

  [Fact]
  public async Task OffRoutePreviewUsesGpsWithoutPretendingTheFullRouteIsRemainingMileage()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    f.Location!.Longitude = -78;
    var preview = await f.Preview();
    Assert.Equal(new RoutePoint(41.5, -78), f.Routing.LastPoints[0]);
    Assert.Null(preview.SavedRoute);
    Assert.Equal(0, preview.Options[0].DifferenceMiles);
  }

  [Theory]
  [InlineData(1, false, 1)]
  [InlineData(2, false, 2)]
  [InlineData(2, true, 1)]
  public async Task PreviewStartsAtGpsSkipsCompletedPickupAndKeepsOnlyReturnedDistinctRoutes(
    int count,
    bool duplicates,
    int expected
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    f.Routing.OptionCount = count;
    f.Routing.DuplicateOptions = duplicates;
    var preview = await f.Preview();
    Assert.Equal(expected, preview.Options.Count);
    Assert.Equal(f.Location!.UpdatedAt, preview.OriginUpdatedAt);
    Assert.Equal(Guid.Empty, preview.Stops[0].Id);
    Assert.Equal(new RoutePoint(41.5, -80), preview.Stops[0].Point);
    Assert.Equal(f.Load.Stops[1].Id, preview.Stops[1].Id);
    Assert.Equal(preview.Stops.Select(s => s.Point), f.Routing.LastPoints);
    Assert.Equal(50, preview.SavedRoute!.Miles, 4);
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
    Assert.Equal(
      0,
      (await f.Db.Dispatches.AsNoTracking().SingleAsync()).RouteChoiceRevision
    );
  }

  [Fact]
  public async Task SaveReplacesOnlyCurrentRoadAndPreservesFullBaselineForHistory()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    var before = await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    var preview = await f.Preview();
    var calls = f.Routing.Calls;
    await f.Choices.SaveAsync(
      f.Load.Id,
      f.Owner,
      new(preview.Id, 2, 0),
      default
    );
    Assert.Equal(calls, f.Routing.Calls);
    var baseline = await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    Assert.Equal(before.RouteJson, baseline.RouteJson);
    var entity = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    var plan = SavedRouteReader.Plan(entity.PlanJson)!;
    Assert.True(plan.FromCurrentPosition);
    Assert.Single(plan.Stops);
    Assert.Equal(f.Load.Stops[1].Id, plan.Stops[0].Id);
    Assert.Contains(f.Load.Stops[0].Id, plan.Tracking.PassedStopIds);
    Assert.Equal(
      preview.Options[1].Route.Legs[0].Points,
      plan.Route.Legs[0].Points
    );
    var load = await f.Planning.Routes.LoadAsync(f.Load.Id, default);
    Assert.Equal(
      RoutePlanningService.HashInputs(load, plan.Profile),
      entity.InputHash
    );
    var saved = JsonSerializer.Deserialize<SavedRouteChoice>(
      (await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync()).ChoiceJson,
      RoutePlanningService.Json
    )!;
    Assert.Equal(100, saved.Route.Miles);
    Assert.Equal(110, saved.Remaining!.Route.Miles);
    var choice = f.Planning.BaseRoutes;
    var continuing = await choice.CurrentAsync(
      load,
      plan.Profile,
      plan.Route.Legs[0].Points[1],
      plan.Stops,
      default
    );
    Assert.Equal(calls, f.Routing.Calls);
    Assert.Equal(plan.Route.Legs[0].Points[^1], continuing.Legs[0].Points[^1]);
    Assert.Equal(
      100,
      (await choice.EnsureAsync(load, plan.Profile, default)).Miles
    );
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("stale")]
  [InlineData("future")]
  public async Task StartedLoadCannotSilentlyReturnToItsOriginalPickupWhenGpsIsUnavailable(
    string reason
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    if (reason == "missing")
      f.Location = null;
    else
      f.Location!.UpdatedAt = f
        .Clock.GetUtcNow()
        .UtcDateTime.AddMinutes(reason == "stale" ? -11 : 2);
    var calls = f.Routing.Calls;
    await Assert.ThrowsAsync<RoutePlanningException>(() => f.Preview());
    Assert.Equal(calls, f.Routing.Calls);
    Assert.Empty(await f.Db.DispatchRoutePreviews.ToListAsync());
  }

  [Theory]
  [InlineData("location")]
  [InlineData("undo")]
  [InlineData("version")]
  public async Task ChangedLocationProgressOrRoadCannotSaveStaleRemainingChoice(
    string reason
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.StartTripAsync();
    var preview = await f.Preview();
    if (reason == "location")
      f.Location!.Latitude = 42;
    else if (reason == "undo")
    {
      var stop = await f.Db.DispatchStops.SingleAsync(s =>
        s.Id == f.Load.Stops[0].Id
      );
      stop.ManualCompletedAt = null;
      stop.ManualCompletionRevision++;
      await f.Db.SaveChangesAsync();
    }
    else
    {
      var entity = await f.Db.DispatchRoutePlans.SingleAsync();
      var plan = SavedRouteReader.Plan(entity.PlanJson)!;
      plan.Version++;
      entity.PlanJson = RoutePlanStorage.Serialize(plan);
      await f.Db.SaveChangesAsync();
      f.Planning.Reads.Invalidate($"route:{f.Load.Id}");
    }
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(f.Load.Id, f.Owner, new(preview.Id, 1, 0), default)
    );
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }
}
