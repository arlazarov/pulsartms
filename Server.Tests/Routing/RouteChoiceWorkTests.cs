using Domain.Entities.Execution;
using Domain.Models.Execution;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChoiceWorkTests
{
  [Fact]
  public async Task ChangedWorkDuringPreviewPreservesThePreviousDraft()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var previous = await f.Preview();
    f.Routing.BeforeCalculate = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "changed"));
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(f.Preview);

    Assert.Contains("work changed", error.Message);
    Assert.Equal(
      previous.Id,
      (await f.Db.DispatchRoutePreviews.AsNoTracking().SingleAsync()).PreviewId
    );
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }

  [Theory]
  [InlineData("queue")]
  [InlineData("configuration")]
  [InlineData("actuals")]
  public async Task SaveRejectsChangedWorkWithoutCacheInvalidation(
    string change
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var first = await f.Preview();
    await f.Choices.SaveAsync(f.Load.Id, f.Owner, new(first.Id, 1, 0), default);
    var preview = await f.Preview();
    var before = (
      await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync()
    ).ChoiceJson;
    var calls = f.Routing.Calls;
    switch (change)
    {
      case "queue":
        f.Db.Dispatches.Add(
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = f.Truck.Id,
            Status = "assigned",
            LoadNumber = 1384,
          }
        );
        await f.Db.SaveChangesAsync();
        break;
      case "configuration":
        await f
          .Db.Trucks.Where(x => x.Id == f.Truck.Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(
              x => x.ConfigurationRevision,
              x => x.ConfigurationRevision + 1
            )
          );
        break;
      case "actuals":
        await f
          .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.ArrivedAt, DateTime.UtcNow)
          );
        break;
    }

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(
          f.Load.Id,
          f.Owner,
          new(preview.Id, 2, preview.Revision),
          default
        )
    );

    Assert.Contains("work changed", error.Message);
    var saved = await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync();
    Assert.Equal(before, saved.ChoiceJson);
    Assert.Equal(1, saved.Revision);
    Assert.Equal(calls, f.Routing.Calls);
  }

  [Fact]
  public async Task OldDraftWithoutWorkStampRequiresANewPreview()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var preview = await f.Preview();
    var draft = await f.Drafts.GetAsync(
      preview.Id,
      f.Owner,
      f.Load.Id,
      default
    );
    await f.Drafts.StoreAsync(draft with { Work = null }, default);

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(f.Load.Id, f.Owner, new(preview.Id, 1, 0), default)
    );

    Assert.Contains("preview needs refreshing", error.Message);
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }

  [Fact]
  public async Task NativeBuildAndPreviewRetainTheResolvedLeg()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = f.Truck.Id,
      Trip = new() { Id = Guid.NewGuid() },
      Status = "active",
      Revision = 3,
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          StartVisitId = f.Load.Stops[0].Id,
          EndVisitId = f.Load.Stops[^1].Id,
        },
      ],
    };
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);

    var plan = await f.Planning.Routes.BuildAsync(
      f.Load.Id,
      new(profile),
      default
    );
    f.Location = new()
    {
      TruckId = f.Truck.Id,
      Latitude = 40,
      Longitude = -80,
      UpdatedAt = f.Clock.GetUtcNow().UtcDateTime,
    };
    var preview = await f.Choices.PreviewAsync(
      f.Load.Id,
      f.Owner,
      new([], ExecutionLegId: leg.Id),
      default
    );
    await f
      .Db.ExecutionLegs.Where(x => x.Id == leg.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, 4));
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        f.Choices.SaveAsync(
          f.Load.Id,
          f.Owner,
          new(preview.Id, 1, 0, leg.Id),
          default
        )
    );

    Assert.Equal(leg.Id, plan.ExecutionLegId);
    Assert.Equal(3, plan.AssignmentRevision);
    Assert.Equal(leg.Id, preview.ExecutionLegId);
    Assert.Contains("work changed", error.Message);
    Assert.Empty(await f.Db.DispatchRouteChoices.ToListAsync());
  }
}
