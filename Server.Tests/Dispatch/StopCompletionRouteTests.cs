using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StopCompletionRouteTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompletingPrefixAndUndoKeepRoadRevisionWithoutProviderCalls(
    bool fromCurrent
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = await SaveRoadAsync(f, fromCurrent);
    Assert.True(
      (
        await f.Handler()
          .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default)
      ).Success
    );
    var completed = await ReadAsync(f);
    AssertRoadUnchanged(before, completed);
    Assert.Equal(f.Load.Stops[1].Id, completed.Tracking.NextStopId);
    Assert.Equal(
      f.Load.Stops[0].Id,
      Assert.Single(completed.Tracking.PassedStopIds)
    );
    await AssertCurrentAsync(f);

    Assert.True(
      (await f.Handler().Handle(f.Command(0, null), default)).Success
    );
    var undone = await ReadAsync(f);
    AssertRoadUnchanged(before, undone);
    Assert.Equal(f.Load.Stops[0].Id, undone.Tracking.NextStopId);
    Assert.Empty(undone.Tracking.PassedStopIds);
    await AssertCurrentAsync(f);
  }

  [Fact]
  public async Task InteriorCompletionChangesRemainingItineraryAndDoesNotBlessOldRoad()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = await SaveRoadAsync(f);
    var saved = await f.Db.DispatchRoutePlans.SingleAsync();
    var hash = saved.InputHash;
    Assert.True(
      (
        await f.Handler()
          .Handle(f.Command(2, f.Clock.GetUtcNow().AddHours(-1)), default)
      ).Success
    );
    Assert.Equal(hash, saved.InputHash);
    Assert.NotEqual(
      RoutePlanInputs.Hash(f.Load, before.Profile),
      saved.InputHash
    );
    AssertRoadUnchanged(before, await ReadAsync(f));
  }

  [Fact]
  public async Task UndoOfStopMissingFromCurrentRoadRequiresNewGeometry()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Assert.True(
      (
        await f.Handler()
          .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default)
      ).Success
    );
    var before = await SaveRoadAsync(f, true, skip: 1);
    var hash = (await f.Db.DispatchRoutePlans.SingleAsync()).InputHash;
    Assert.True(
      (await f.Handler().Handle(f.Command(0, null), default)).Success
    );
    Assert.Equal(hash, (await f.Db.DispatchRoutePlans.SingleAsync()).InputHash);
    Assert.NotEqual(RoutePlanInputs.Hash(f.Load, before.Profile), hash);
  }

  [Theory]
  [InlineData("address")]
  [InlineData("order")]
  [InlineData("truck")]
  public async Task CompletionCannotHideExistingRoadInputChanges(string change)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = await SaveRoadAsync(f);
    var hash = (await f.Db.DispatchRoutePlans.SingleAsync()).InputHash;
    if (change == "address")
      f.Load.Stops[1].Address = "Changed destination";
    if (change == "order")
      f.Load.Stops[1].Sequence = 6;
    if (change == "truck")
    {
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = "other",
        ExternalId = "other",
      };
      f.Db.Trucks.Add(truck);
      f.Load.TruckId = truck.Id;
    }
    await f.Db.SaveChangesAsync();
    Assert.True(
      (
        await f.Handler()
          .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default)
      ).Success
    );
    Assert.Equal(hash, (await f.Db.DispatchRoutePlans.SingleAsync()).InputHash);
    Assert.NotEqual(RoutePlanInputs.Hash(f.Load, before.Profile), hash);
  }

  [Fact]
  public async Task ConcurrentRoadReplacementRollsBackCompletionAndAuditTogether()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = await SaveRoadAsync(f);
    await using var stale = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(f.Db.Database.GetDbConnection())
        .Options
    );
    await stale.DispatchRoutePlans.SingleAsync();
    var current = await f.Db.DispatchRoutePlans.SingleAsync();
    before.Version++;
    current.PlanJson = RoutePlanStorage.Serialize(before);
    await f.Db.SaveChangesAsync();

    var result = await f.Handler(db: stale)
      .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default);

    Assert.Equal(409, result.StatusCode);
    Assert.Empty(await f.Db.DispatchStopCompletionEvents.ToListAsync());
    var stop = await f
      .Db.DispatchStops.AsNoTracking()
      .SingleAsync(x => x.Id == f.Load.Stops[0].Id);
    Assert.Null(stop.ManualCompletedAt);
    Assert.Equal(0, stop.ManualCompletionRevision);
    Assert.Equal(before.Version, (await ReadAsync(f)).Version);
  }

  [Fact]
  public async Task CompletingAllStopsKeepsRoadAndClearsNextStop()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = await SaveRoadAsync(f);
    for (var i = 0; i < f.Load.Stops.Count; i++)
      Assert.True(
        (
          await f.Handler()
            .Handle(f.Command(i, f.Clock.GetUtcNow().AddHours(-1)), default)
        ).Success
      );
    var completed = await ReadAsync(f);
    AssertRoadUnchanged(before, completed);
    Assert.True(completed.Tracking.AllStopsPassed);
    Assert.Null(completed.Tracking.NextStopId);
    await AssertCurrentAsync(f);
  }

  private static async Task<RoutePlan> SaveRoadAsync(
    StopCompletionFixture f,
    bool fromCurrent = false,
    int skip = 0
  )
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11007",
      ExternalId = "completion-fixture",
    };
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    var profile = await f.Planning.Routes.ProfileAsync(truck.Id, default);
    var stops = f
      .Load.Stops.Select(
        (s, i) =>
          new PlanStop(s.Id, s.Name, s.Address, s.Sequence, new(40, -80 + i))
      )
      .ToList();
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      TruckId = truck.Id,
      Version = 7,
      CalculatedAt = DateTime.UtcNow,
      Profile = profile,
      FromCurrentPosition = fromCurrent,
      Stops = stops.Skip(skip).ToList(),
      ReferenceStops = skip > 0 ? stops : null,
      Route = new()
      {
        Miles = 400,
        Seconds = 24000,
        Legs = [new(400, 24000, [new(40, -80), new(40, -76)])],
      },
    };
    f.Db.DispatchRoutePlans.Add(
      new DispatchRoutePlan
      {
        Id = plan.Id,
        DispatchId = f.Load.Id,
        TruckId = truck.Id,
        InputHash = RoutePlanInputs.Hash(f.Load, profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
        CreatedAt = plan.CalculatedAt,
      }
    );
    await f.Db.SaveChangesAsync();
    return plan;
  }

  private static async Task<RoutePlan> ReadAsync(StopCompletionFixture f) =>
    RoutePlanStorage.Read(
      (
        await RoutePlanStorage.LoadAsync(
          f.Db,
          await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync(),
          default
        )
      )!
    )!;

  private static async Task AssertCurrentAsync(StopCompletionFixture f)
  {
    var saved = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    Assert.Equal(
      RoutePlanInputs.Hash(
        f.Load,
        await f.Planning.Routes.ProfileAsync(f.Load.TruckId!.Value, default)
      ),
      saved.InputHash
    );
  }

  private static void AssertRoadUnchanged(RoutePlan before, RoutePlan after)
  {
    Assert.Equal(before.Id, after.Id);
    Assert.Equal(before.Version, after.Version);
    Assert.Equal(before.CalculatedAt, after.CalculatedAt);
    Assert.Equal(
      RoutePlanStorage.Serialize(before.Route),
      RoutePlanStorage.Serialize(after.Route)
    );
    Assert.Equal(before.Stops.Select(x => x.Id), after.Stops.Select(x => x.Id));
    Assert.False(after.InputsChanged);
  }
}
