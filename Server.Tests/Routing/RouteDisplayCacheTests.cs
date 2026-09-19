using System.Text.Json;
using Application.Caching;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public class RouteDisplayCacheTests
{
  [Fact]
  public async Task ColdLoadsAreBoundedWithoutBlockingHotSnapshotsOrCancellation()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var stripes = new HashSet<uint>();
    var ids = new List<Guid>();
    while (ids.Count < 4)
    {
      var id = Guid.NewGuid();
      if (stripes.Add((uint)id.GetHashCode() % 64))
        ids.Add(id);
    }
    var hot = await cache.GetAsync(
      ids[0],
      () => Task.FromResult<DispatchRoutePlan?>(new() { PlanJson = "{}" }),
      default
    );
    var opened = 0;
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    async Task<DispatchRoutePlan?> Load()
    {
      if (Interlocked.Increment(ref opened) == 2)
        started.TrySetResult();
      await release.Task;
      return new() { PlanJson = "{}" };
    }
    var first = cache.GetAsync(ids[1], Load, default);
    var second = cache.GetAsync(ids[2], Load, default);
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      using var cancellation = new CancellationTokenSource();
      var waiting = cache.GetAsync(ids[3], Load, cancellation.Token);
      Assert.Equal(2, Volatile.Read(ref opened));
      Assert.False(waiting.IsCompleted);
      Assert.Same(
        hot,
        await cache.GetAsync(
          ids[0],
          () => throw new InvalidOperationException(),
          default
        )
      );
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
      Assert.Equal(2, Volatile.Read(ref opened));
    }
    finally
    {
      release.TrySetResult();
      await Task.WhenAll(first, second);
    }
    Assert.NotNull(await cache.GetAsync(ids[3], Load, default));
    Assert.Equal(3, opened);
  }

  [Fact]
  public async Task ProjectionPreservesExactMatchingAndReloadsAfterInvalidation()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var id = Guid.NewGuid();
    var points = Enumerable
      .Range(0, 10001)
      .Select(i => new RoutePoint(
        40 + Math.Sin(i / 100d) * .00001,
        -80 + i / 10000d
      ))
      .ToList();
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = id,
      Version = 1,
      Route = new()
      {
        Miles = 100,
        Seconds = 7200,
        Points = points,
        Legs = [new(100, 7200, points)],
      },
    };
    var entity = new DispatchRoutePlan
    {
      DispatchId = id,
      PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json),
    };
    var calls = 0;
    Task<DispatchRoutePlan?> Load()
    {
      calls++;
      return Task.FromResult<DispatchRoutePlan?>(entity);
    }
    var snapshot = (await cache.GetAsync(id, Load, default))!;
    var exact = new RouteGeometry(plan.Route);
    foreach (
      var position in new[]
      {
        new RoutePoint(40, -79.5),
        new RoutePoint(40.01, -79.2),
      }
    )
      Assert.Equal(exact.Match(position), snapshot.Geometry.Match(position));
    Assert.True(snapshot.DisplayBytes < entity.PlanJson.Length / 10);
    var display = snapshot.ReadPlan();
    Assert.Empty(display.Route.Points);
    Assert.Equal(100, display.Route.Miles);
    Assert.Equal(7200, display.Route.Seconds);
    display.Route.Legs.Clear();
    Assert.Single(snapshot.ReadPlan().Route.Legs);
    Assert.Same(snapshot, await cache.GetAsync(id, Load, default));
    Assert.Equal(1, calls);
    reads.Invalidate($"route:{id}");
    Assert.NotSame(snapshot, await cache.GetAsync(id, Load, default));
    Assert.Equal(2, calls);
  }

  [Theory]
  [InlineData(true, 3, true)]
  [InlineData(true, 2, false)]
  [InlineData(false, 3, false)]
  public void SnapshotSelectsOnlyExactKnownGeometryAndClonesAllMutableMetadata(
    bool sameId,
    int version,
    bool omitted
  )
  {
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Version = 3,
      Route = new()
      {
        Miles = 10,
        Seconds = 600,
        Legs = [new(10, 600, [new(40, -80), new(41, -80)])],
      },
      ReferenceRoute = new()
      {
        Miles = 20,
        Legs = [new(20, 1200, [new(39, -80), new(41, -80)])],
      },
      Stops = [new(Guid.NewGuid(), "Saved", "Address", 1, new(41, -80))],
      FuelPlan = new() { Notes = ["Saved note"] },
      Tracking = new() { NextStopLabel = "Saved stop" },
    };
    var snapshot = RouteDisplayCache.Create(
      new()
      {
        Id = plan.Id,
        DispatchId = plan.DispatchId,
        TruckId = plan.TruckId,
        InputHash = "saved-input",
        PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json),
      }
    );
    var display = snapshot.ReadPlan(sameId ? plan.Id : Guid.NewGuid(), version);
    Assert.Equal(omitted, display.GeometryOmitted);
    Assert.Equal(omitted ? 0 : 2, display.Route.Legs[0].Points.Count);
    Assert.Equal(omitted ? 0 : 2, display.ReferenceRoute!.Legs[0].Points.Count);
    Assert.Equal(10, display.Route.Miles);
    Assert.Equal(600, display.Route.Legs[0].Seconds);
    var metadata = snapshot.Metadata;
    metadata.InputHash = "mutated";
    metadata.TruckId = Guid.NewGuid();
    display.Route.Legs.Clear();
    display.ReferenceRoute.Legs.Clear();
    display.Stops.Clear();
    display.FuelPlan!.Notes.Clear();
    display.Tracking.NextStopLabel = "Mutated";
    display.Profile.HeightFeet = 99;
    var next = snapshot.ReadPlan(plan.Id, plan.Version);
    Assert.True(next.GeometryOmitted);
    Assert.Single(next.Route.Legs);
    Assert.Single(next.ReferenceRoute!.Legs);
    Assert.Equal("Saved", Assert.Single(next.Stops).Name);
    Assert.Equal("Saved note", Assert.Single(next.FuelPlan!.Notes));
    Assert.Equal("Saved stop", next.Tracking.NextStopLabel);
    Assert.NotEqual(99, next.Profile.HeightFeet);
    Assert.Equal("saved-input", snapshot.Metadata.InputHash);
    Assert.Equal(plan.TruckId, snapshot.Metadata.TruckId);
    Assert.Equal(2, snapshot.ReadPlan().Route.Legs[0].Points.Count);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public void MetadataOnlyRecommendationProjectionKeepsFullGeometryFallbackForFreshAndOffRoutePositions(
    bool stale,
    bool offRoute
  )
  {
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      Version = 1,
      Route = new()
      {
        Miles = 100,
        Legs = [new(100, 6000, [new(40, -80), new(40, -79)])],
      },
      InputsChanged = true,
      FuelRecommendations = new()
      {
        Stations =
        [
          new() { Name = "Behind", RouteMile = 20 },
          new() { Name = "Ahead", RouteMile = 80 },
        ],
      },
    };
    var snapshot = RouteDisplayCache.Create(
      new()
      {
        PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json),
      }
    );
    var progress = new RouteProgress(
      null,
      null,
      null,
      offRoute ? 2 : 0,
      offRoute,
      stale,
      DateTime.UtcNow,
      new(offRoute ? 40.02 : 40, -79.5)
    );
    var full = new RoutePlanningState(
      new(),
      snapshot.ReadPlan(),
      progress,
      null,
      null,
      true
    );
    var metadata = full with
    {
      Plan = snapshot.ReadPlan(plan.Id, plan.Version),
    };
    AutomaticPlanningService.ProjectRecommendations(full);
    AutomaticPlanningService.ProjectRecommendations(
      metadata,
      snapshot.Geometry
    );
    Assert.Equal(
      full.Plan!.FuelRecommendations!.Stations.Select(x => x.Name),
      metadata.Plan!.FuelRecommendations!.Stations.Select(x => x.Name)
    );
    Assert.Equal(
      stale ? 2 : 1,
      metadata.Plan.FuelRecommendations.Stations.Count
    );
    Assert.All(
      metadata.Plan.FuelRecommendations.Stations,
      station => Assert.Null(station.MilesAhead)
    );
    Assert.Empty(metadata.Plan.Route.Legs[0].Points);
  }

  [Fact]
  public async Task IndependentColdKeysLoadConcurrentlyAndSameKeySharesTheLoadWithCancelledWaiters()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var firstId = Guid.NewGuid();
    var otherId = Enumerable
      .Range(0, 1000)
      .Select(_ => Guid.NewGuid())
      .First(id =>
        (uint)id.GetHashCode() % 64 != (uint)firstId.GetHashCode() % 64
      );
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var calls = 0;
    async Task<DispatchRoutePlan?> Load()
    {
      Interlocked.Increment(ref calls);
      started.TrySetResult();
      await release.Task;
      return new()
      {
        DispatchId = firstId,
        PlanJson = JsonSerializer.Serialize(
          new RoutePlan { DispatchId = firstId },
          RoutePlanningService.Json
        ),
      };
    }
    var first = cache.GetAsync(firstId, Load, default);
    await started.Task;
    using var cancelled = new CancellationTokenSource();
    var waiter = cache.GetAsync(firstId, Load, cancelled.Token);
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
    var same = cache.GetAsync(firstId, Load, default);
    try
    {
      var unrelated = await cache
        .GetAsync(
          otherId,
          () =>
            Task.FromResult<DispatchRoutePlan?>(
              new() { DispatchId = otherId, PlanJson = "{}" }
            ),
          default
        )
        .WaitAsync(TimeSpan.FromSeconds(2));
      Assert.NotNull(unrelated);
      Assert.False(first.IsCompleted);
      Assert.False(same.IsCompleted);
    }
    finally
    {
      release.TrySetResult();
    }
    Assert.Same(await first, await same);
    Assert.Equal(1, calls);
  }

  [Fact]
  public async Task InvalidationDuringColdLoadCannotPopulateTheNewGeneration()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var id = Guid.NewGuid();
    var reply = new TaskCompletionSource<DispatchRoutePlan?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var old = cache.GetAsync(id, () => reply.Task, default);
    reads.Invalidate($"route:{id}");
    reply.SetResult(
      new()
      {
        PlanJson = JsonSerializer.Serialize(
          new RoutePlan { Id = id, Version = 1 },
          RoutePlanningService.Json
        ),
      }
    );
    var first = await old;
    var latest = await cache.GetAsync(
      id,
      () =>
        Task.FromResult<DispatchRoutePlan?>(
          new()
          {
            PlanJson = JsonSerializer.Serialize(
              new RoutePlan { Id = id, Version = 2 },
              RoutePlanningService.Json
            ),
          }
        ),
      default
    );
    Assert.NotSame(first, latest);
    Assert.False(latest!.ReadPlan(id, 1).GeometryOmitted);
    Assert.True(latest.ReadPlan(id, 2).GeometryOmitted);
  }

  [Fact]
  public async Task OversizedSnapshotsAndFailedLoadsAreNotRetained()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var id = Guid.NewGuid();
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        cache.GetAsync(
          id,
          () => throw new InvalidOperationException("synthetic failure"),
          default
        )
    );
    var entity = new DispatchRoutePlan
    {
      InputHash = new string('x', 16 * 1024 * 1024),
      PlanJson = "{}",
    };
    var calls = 0;
    Task<DispatchRoutePlan?> Load()
    {
      calls++;
      return Task.FromResult<DispatchRoutePlan?>(entity);
    }
    var first = await cache.GetAsync(id, Load, default);
    var second = await cache.GetAsync(id, Load, default);
    Assert.True(first!.Size > 32 * 1024 * 1024);
    Assert.NotSame(first, second);
    Assert.Equal(2, calls);
  }
}
