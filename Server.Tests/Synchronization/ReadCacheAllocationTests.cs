using System.Diagnostics;
using System.Text.Json;
using Application.Caching;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Server.Tests.Synchronization;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Allocation")]
public class ReadCacheAllocationTests(ITestOutputHelper output)
{
  [Fact]
  public async Task WarmLargeSnapshotAvoidsJsonSizedAllocations()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var entity = new DispatchRoutePlan
    {
      PlanJson =
        "{\"points\":["
        + string.Join(
          ',',
          Enumerable.Repeat(
            "{\"latitude\":43.123456,\"longitude\":-79.123456}",
            60_000
          )
        )
        + "]}",
    };
    Task<DispatchRoutePlan> Load() => Task.FromResult(entity);
    await cache.GetAsync("route", "benchmark", Load);
    var wrapped = JsonSerializer.Serialize(entity);
    _ = JsonSerializer.Deserialize<DispatchRoutePlan>(wrapped);
    _ = await cache.GetAsync("route", "benchmark", Load);
    const int count = 20;
    var before = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < count; i++)
      Assert.Equal(
        entity.PlanJson.Length,
        JsonSerializer.Deserialize<DispatchRoutePlan>(wrapped)!.PlanJson.Length
      );
    watch.Stop();
    var previousBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    var previousMs = watch.Elapsed.TotalMilliseconds;
    before = GC.GetAllocatedBytesForCurrentThread();
    watch.Restart();
    for (var i = 0; i < count; i++)
      Assert.Same(
        entity.PlanJson,
        (await cache.GetAsync("route", "benchmark", Load)).PlanJson
      );
    watch.Stop();
    var currentBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    output.WriteLine(
      $"Payload characters: {entity.PlanJson.Length}; reads: {count}"
    );
    output.WriteLine(
      $"Previous snapshot cloning: {previousBytes:N0} allocated bytes, {previousMs:F2} ms"
    );
    output.WriteLine(
      $"Current snapshot cloning: {currentBytes:N0} allocated bytes, {watch.Elapsed.TotalMilliseconds:F2} ms"
    );
    Assert.True(currentBytes < previousBytes / 100);
  }
}
