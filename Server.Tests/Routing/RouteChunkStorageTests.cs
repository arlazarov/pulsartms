using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChunkStorageTests
{
  [Fact]
  public async Task StateWritesKeepChunksAndManifestAndExactMeasures()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    var plan = Plan(f);
    var expected = plan.Route.Legs[0].Points.ToArray();
    await store.SaveBuiltAsync(null, plan, "fixture", default);
    f.Db.ChangeTracker.Clear();
    var entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    Assert.DoesNotContain("latitude", entity.PlanJson);
    Assert.NotNull(entity.GeometryManifestJson);
    var count = await f.Db.RouteGeometryChunks.CountAsync();
    Assert.Equal(8, count);
    var manifest = entity.GeometryManifestJson;
    var restored = RoutePlanStorage.Read(entity)!;
    Assert.Equal(expected, restored.Route.Legs[0].Points);
    Assert.Equal(expected, restored.ReferenceRoute!.Legs[0].Points);
    Assert.Equal(4000, restored.Route.Miles);
    Assert.Equal(240000, restored.Route.Seconds);
    restored.Tracking.OffRouteSince = DateTime.UtcNow;
    await store.SaveAsync(entity, restored, default);
    Assert.Equal(count, await f.Db.RouteGeometryChunks.CountAsync());
    Assert.Single(await f.Db.RouteGeometryChanges.ToListAsync());
    Assert.Equal(manifest, entity.GeometryManifestJson);
    Assert.DoesNotContain(
      f.Db.ChangeTracker.Entries<RouteGeometryChunk>(),
      x => x.State != EntityState.Unchanged
    );
  }

  [Fact]
  public async Task DetourReusesExactPrefixSuffixAndCanReturnToAnOldRoad()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    var plan = Plan(f);
    var original = plan.Route.Legs[0].Points.ToArray();
    await store.SaveBuiltAsync(null, plan, "fixture", default);
    f.Db.ChangeTracker.Clear();
    var entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var oldKeys = entity.GeometryChunks.Select(x => x.Key).ToHashSet();
    plan = RoutePlanStorage.Read(entity)!;
    var changed = original.ToList();
    changed[510] = new(41, -70);
    plan.Route.Legs[0] = plan.Route.Legs[0] with { Points = changed };
    plan.Version++;
    await store.SaveAsync(entity, plan, default);
    Assert.Equal(9, await f.Db.RouteGeometryChunks.CountAsync());
    Assert.Equal(2, await f.Db.RouteGeometryChanges.CountAsync());
    f.Db.ChangeTracker.Clear();
    entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var restored = RoutePlanStorage.Read(entity)!;
    Assert.Equal(changed, restored.Route.Legs[0].Points);
    Assert.Equal(original, restored.ReferenceRoute!.Legs[0].Points);
    Assert.Subset(
      entity.GeometryChunks.Select(x => x.Key).ToHashSet(),
      oldKeys
    );
    restored.Route.Legs[0] = restored.Route.Legs[0] with
    {
      Points = original.ToList(),
    };
    restored.Version++;
    await store.SaveAsync(entity, restored, default);
    Assert.Equal(9, await f.Db.RouteGeometryChunks.CountAsync());
    Assert.Equal(3, await f.Db.RouteGeometryChanges.CountAsync());
    var firstRoad = await RoutePlanStorage.ReadRoadAtAsync(
      f.Db,
      entity.Id,
      1,
      default
    );
    var detourRoad = await RoutePlanStorage.ReadRoadAtAsync(
      f.Db,
      entity.Id,
      2,
      default
    );
    Assert.Equal(original, firstRoad!.Legs[0].Points);
    Assert.Equal(changed, detourRoad!.Legs[0].Points);
    await store.SaveAsync(entity, restored, default);
    Assert.Equal(3, await f.Db.RouteGeometryChanges.CountAsync());
  }

  [Fact]
  public async Task StaleStateCannotReplaceNewerManifest()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    await store.SaveBuiltAsync(null, Plan(f), "fixture", default);
    f.Db.ChangeTracker.Clear();
    var a = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var b = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var changed = RoutePlanStorage.Read(a)!;
    changed.Version++;
    changed.Route.Legs[0].Points[100] = new(42, -72);
    await store.SaveAsync(a, changed, default);
    f.Db.ChangeTracker.Clear();
    var stale = RoutePlanStorage.Read(b)!;
    stale.Tracking.OffRouteSince = DateTime.UtcNow;
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
      () => store.SaveAsync(b, stale, default)
    );
    Assert.Equal(2, await f.Db.RouteGeometryChanges.CountAsync());
  }

  [Fact]
  public async Task LegacyInlineRoadConvertsWithoutChangingProgress()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var plan = Plan(f);
    var entity = new DispatchRoutePlan
    {
      Id = plan.Id,
      DispatchId = f.Load.Id,
      TruckId = f.Truck.Id,
      PlanJson = RoutePlanStorage.Serialize(plan),
    };
    f.Db.DispatchRoutePlans.Add(entity);
    await f.Db.SaveChangesAsync();
    var old = RoutePlanStorage.Read(entity)!;
    var expected = new RouteGeometry(old.Route).Match(new(40, -79.5));
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    await store.SaveAsync(entity, old, default);
    f.Db.ChangeTracker.Clear();
    entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var after = new RouteGeometry(RoutePlanStorage.Read(entity)!.Route).Match(
      new(40, -79.5)
    );
    Assert.Equal(expected, after);
  }

  [Fact]
  public async Task MovementCheckpointSurvivesRestartAndReadsWithoutProviders()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    await store.SaveBuiltAsync(null, Plan(f), "fixture", default);
    f.Db.ChangeTracker.Clear();
    var entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var plan = RoutePlanStorage.Read(entity)!;
    var geometry = new RouteGeometry(plan.Route);
    var start = DateTime.UtcNow.AddMinutes(-1);
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      entity.GeometryRevision,
      new(start, new(42, -80))
    );
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      entity.GeometryRevision,
      new(start.AddSeconds(5), new(42, -79.999))
    );
    await store.SaveAsync(entity, plan, default);
    f.Db.ChangeTracker.Clear();
    entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    plan = RoutePlanStorage.Read(entity)!;
    var openId = plan.Tracking.Movement!.Id;
    Assert.Null(RouteDisplayCache.Create(entity).ReadPlan().Tracking.Movement);
    Assert.NotNull(RoutePlanStorage.Read(entity)!.Tracking.Movement);
    RouteMovementRecorder.Observe(
      plan,
      geometry,
      entity.GeometryRevision,
      new(start.AddSeconds(5), new(42, -79.999))
    );
    Assert.Equal(2, plan.Tracking.Movement.Observations.Count);
    RouteMovementRecorder.Close(plan);
    await store.SaveAsync(entity, plan, default);
    var row = await f.Db.RouteMovementChunks.SingleAsync();
    Assert.Equal(openId, row.Id);
    Assert.Equal(f.Truck.Id, row.TruckId);
    var handler = new GetTruckMovementHandler(f.Db);
    var result = await handler.Handle(
      new(f.Truck.Id, start.AddMinutes(-1), start.AddMinutes(2)),
      default
    );
    Assert.True(result.Success);
    Assert.False(result.Response!.Truncated);
    Assert.Equal(openId, Assert.Single(result.Response.Segments).Movement.Id);
    await store.SaveAsync(entity, plan, default);
    Assert.Single(await f.Db.RouteMovementChunks.ToListAsync());
  }

  [Fact]
  public async Task SameCoordinatesWithNewMeasuresInvalidateTheExactIndex()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    await store.SaveBuiltAsync(null, Plan(f), "fixture", default);
    f.Db.ChangeTracker.Clear();
    var entity = (await store.ReadUncachedAsync(f.Load.Id, default))!;
    var plan = RoutePlanStorage.Read(entity)!;
    var before = f.Planning.Displays.ExactGeometry(entity, plan);
    plan.Route.Legs[0] = plan.Route.Legs[0] with { Miles = 3000 };
    plan.Route.Miles = 3000;
    await store.SaveAsync(entity, plan, default);
    var after = f.Planning.Displays.ExactGeometry(entity, plan);
    Assert.NotSame(before, after);
    Assert.Equal(3000, after.Miles, 8);
    Assert.Equal(8, await f.Db.RouteGeometryChunks.CountAsync());
  }

  [Fact]
  public async Task AReusedRevisionCannotReuseAnUncommittedGeometryIndex()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var entity = new DispatchRoutePlan
    {
      Id = Guid.NewGuid(),
      GeometryRevision = 1,
      GeometryManifestJson = "first",
    };
    var plan = Plan(f);
    var first = f.Planning.Displays.ExactGeometry(entity, plan);
    entity.GeometryManifestJson = "replacement";
    plan.Route.Legs[0] = plan.Route.Legs[0] with { Miles = 3000 };
    var replacement = f.Planning.Displays.ExactGeometry(entity, plan);
    Assert.NotSame(first, replacement);
    Assert.Equal(3000, replacement.Miles, 8);
  }

  private static RoutePlan Plan(RouteChoiceFixture f)
  {
    var points = Enumerable
      .Range(0, 1000)
      .Select(i => new RoutePoint(40 + Math.Sin(i) * .001, -80 + i * .001))
      .ToList();
    var route = new TruckRoute
    {
      Miles = 4000,
      Seconds = 240000,
      Legs = [new(4000, 240000, points)],
    };
    return new()
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      TruckId = f.Truck.Id,
      Version = 1,
      Route = route,
      ReferenceRoute = route,
    };
  }
}
