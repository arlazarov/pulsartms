using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Xunit.Abstractions;
using Application.Caching;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Allocation")]
public sealed class RouteDisplayCacheAllocationTests(ITestOutputHelper output)
{
  [Fact]
  public void ConstructingExactGeometryUsesOnlyItsFinalSegmentBuffer()
  {
    var points = Enumerable.Range(0, 50_001).Select(i => new RoutePoint(40, -80 + i / 50000d)).ToList();
    var route = new TruckRoute { Legs = [new(3000, 180000, points)] };
    GC.KeepAlive(new RouteGeometry(route));
    var before = GC.GetAllocatedBytesForCurrentThread();
    var geometry = new RouteGeometry(route);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    output.WriteLine($"Exact geometry, 50,000 segments: {allocated:N0} allocated bytes.");
    Assert.True(allocated < 50_000L * 48 + 4096, $"Unexpected temporary geometry buffer: {allocated:N0} bytes.");
    Assert.Equal(3000, geometry.Miles, 6);
    Assert.Equal(1500, geometry.Match(new(40, -79.5)).Along, 5);
  }

  [Fact]
  public void ReplacedDisplayGeometryIsCollectibleBeforeItsOriginalExpiry()
  {
    using var reads = new ReadCache(Options.Create(new SynchronizationOptions()));
    using var cache = new RouteDisplayCache(reads);
    var id = Guid.NewGuid();
    var previous = Populate(cache, id);
    reads.Invalidate($"route:{id}");
    var current = Populate(cache, id);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Assert.False(previous.TryGetTarget(out _));
    Assert.True(current.TryGetTarget(out _));
    GC.KeepAlive(cache);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference<RouteDisplayCache.Snapshot> Populate(RouteDisplayCache cache, Guid id)
  {
    var plan = new RoutePlan { Id = id, Route = new() { Legs = [new(100, 6000,
      Enumerable.Range(0, 10001).Select(i => new RoutePoint(40, -80 + i / 10000d)).ToList())] } };
    var snapshot = cache.GetAsync(id, () => Task.FromResult<Domain.Entities.Dispatch.DispatchRoutePlan?>(new()
      { PlanJson = RoutePlanStorage.Serialize(plan) }), default).GetAwaiter().GetResult();
    return new(snapshot!);
  }

  [Fact]
  public void MatchingDoesNotAllocateAnObjectForEverySegment()
  {
    var points = Enumerable.Range(0, 10001).Select(i => new RoutePoint(40, -80 + i / 10000d)).ToList();
    var geometry = new RouteGeometry(new() { Legs = [new(100, 7200, points)] });
    var position = new RoutePoint(40, -79.5);
    geometry.Match(position);
    var before = GC.GetAllocatedBytesForCurrentThread();
    var match = geometry.Match(position);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.Equal(50, match.Along, 5);
    Assert.Equal(0, match.Away, 5);
    Assert.True(allocated < 1024);
  }

  [Fact]
  public void MatchingReadsAllocateMetadataRatherThanObjectsForEveryDisplayPoint()
  {
    var id = Guid.NewGuid();
    RouteDisplayCache.Snapshot Create(int count)
    {
      var points = Enumerable.Range(0, count).Select(i => new RoutePoint(40 + Math.Sin(i / 8d) * .0001, -80 + i * .00003)).ToList();
      var plan = new RoutePlan { Id = id, Version = 1, Route = new() { Miles = 100, Seconds = 6000, Legs = [new(100, 6000, points)] },
        ReferenceRoute = new() { Miles = 100, Seconds = 6000, Legs = [new(100, 6000, points)] } };
      return RouteDisplayCache.Create(new() { PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json) });
    }
    var small = Create(3);
    var large = Create(3001);
    static long Measure(RouteDisplayCache.Snapshot snapshot, Guid? knownId)
    {
      for (var i = 0; i < 5; i++) GC.KeepAlive(snapshot.ReadPlan(knownId, 1));
      var before = GC.GetAllocatedBytesForCurrentThread();
      for (var i = 0; i < 10; i++) GC.KeepAlive(snapshot.ReadPlan(knownId, 1));
      return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
    }
    var smallMetadata = Measure(small, id);
    var largeMetadata = Measure(large, id);
    var largeFull = Measure(large, null);
    output.WriteLine($"Per read: small metadata={smallMetadata:N0} B, large metadata={largeMetadata:N0} B, large full={largeFull:N0} B; display={large.DisplayBytes:N0} B, metadata={large.MetadataBytes:N0} B.");
    Assert.True(largeMetadata <= smallMetadata + 2048);
    Assert.True(largeMetadata * 4 < largeFull);
    Assert.True(large.MetadataBytes * 4 < large.DisplayBytes);
  }
}
