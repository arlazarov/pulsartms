using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Routing.Models;
using Infrastructure.Integrations.GeoTimeZone;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaRefreshSignalTests
{
  [Fact]
  public void StaleCleanupCannotRemoveANewerConcurrentResult()
  {
    using var memory = new EtaMemory();
    var id = Guid.NewGuid();
    var now = DateTime.UtcNow;
    var older = new EtaMemory.Entry(
      "older",
      new(now, now.AddMinutes(2), [], null, [])
    );
    var newer = new EtaMemory.Entry(
      "newer",
      new(now.AddSeconds(1), now.AddMinutes(2), [], null, [])
    );
    memory.Results[id] = older;
    memory.Results[id] = newer;
    Assert.False(memory.RemoveIfCurrent(id, older));
    Assert.Same(newer, memory.Results[id]);
    Assert.True(memory.RemoveIfCurrent(id, newer));
    Assert.Empty(memory.Results);
  }

  [Fact]
  public async Task RepeatedSnapshotMissesCoalesceUntilTheInputRevisionChanges()
  {
    using var memory = new EtaMemory();
    var id = Guid.NewGuid();
    memory.Demand(id, "first", DateTime.UtcNow);
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    using var cancelled = new CancellationTokenSource();
    var waiting = memory.WaitForRefreshAsync(cancelled.Token);
    for (var i = 0; i < 10; i++)
      memory.Demand(id, "first", DateTime.UtcNow);
    Assert.False(waiting.IsCompleted);
    memory.Demand(id, "second", DateTime.UtcNow);
    await waiting.WaitAsync(TimeSpan.FromSeconds(1));
  }

  [Fact]
  public async Task NewDemandWakesImmediatelyAndRepeatedViewsDoNotAccelerateRetries()
  {
    var memory = new EtaMemory();
    var id = Guid.NewGuid();
    memory.View(id, DateTime.UtcNow);
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    using var cancelled = new CancellationTokenSource();
    var waiting = memory.WaitForRefreshAsync(cancelled.Token);
    for (var i = 0; i < 10; i++)
      memory.View(id, DateTime.UtcNow);
    Assert.False(waiting.IsCompleted);
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
  }

  [Fact]
  public async Task SignalsCoalesceAndCancelledWaitDoesNotConsumeLaterDemand()
  {
    var memory = new EtaMemory();
    for (var i = 0; i < 10; i++)
      memory.RequestRefresh();
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    using var cancelled = new CancellationTokenSource();
    var waiting = memory.WaitForRefreshAsync(cancelled.Token);
    Assert.False(waiting.IsCompleted);
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    memory.RequestRefresh();
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
  }

  [Fact]
  public async Task InvalidResultWakesOnceAndMissingReadsDoNotKeepWaking()
  {
    var memory = new EtaMemory();
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      Version = 2,
    };
    var state = new RoutePlanningState(new(), plan, null, null, null, true);
    var service = new EtaService(
      null!,
      null!,
      new RouteRegionLookup(),
      memory,
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );
    Assert.Null(service.GetCached(state));
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    memory.Results[plan.DispatchId] = new(
      "old",
      new(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(2), [], null, []),
      "old"
    );
    Assert.Null(service.GetCached(state));
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    using var cancelled = new CancellationTokenSource();
    var waiting = memory.WaitForRefreshAsync(cancelled.Token);
    Assert.Null(service.GetCached(state));
    Assert.False(waiting.IsCompleted);
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
  }
}
