using System.Text.Json;
using Application.Caching;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class ExecutionRouteScopeTests
{
  [Fact]
  public async Task TwoLegsAndLegacyPlanDoNotShareDisplaySnapshots()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var dispatch = Guid.NewGuid();
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var firstSnapshot = await Read(first, 1);
    var secondSnapshot = await Read(second, 2);
    var legacySnapshot = await Read(null, 3);

    Assert.Equal(first, firstSnapshot!.Metadata.ExecutionLegId);
    Assert.Equal(second, secondSnapshot!.Metadata.ExecutionLegId);
    Assert.Null(legacySnapshot!.Metadata.ExecutionLegId);
    Assert.Same(firstSnapshot, await Read(first, 4));
    Assert.Same(secondSnapshot, await Read(second, 4));
    Assert.Same(legacySnapshot, await Read(null, 4));

    reads.Invalidate(RoutePlanStore.CacheKey(dispatch, first));
    Assert.Equal(4, (await Read(first, 4))!.ReadPlan().Version);
    Assert.Same(secondSnapshot, await Read(second, 5));
    Assert.Same(legacySnapshot, await Read(null, 5));

    Task<RouteDisplayCache.Snapshot?> Read(Guid? leg, int version) =>
      cache.GetAsync(
        dispatch,
        () => Task.FromResult<DispatchRoutePlan?>(Row(dispatch, leg, version)),
        default,
        leg
      );
  }

  [Fact]
  public async Task NativeReadRejectsAnotherLegAndLegacyRows()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var cache = new RouteDisplayCache(reads);
    var dispatch = Guid.NewGuid();
    var leg = Guid.NewGuid();

    Assert.Null(await Read(Row(dispatch, Guid.NewGuid(), 1)));
    Assert.Null(await Read(Row(dispatch, null, 1)));
    Assert.Null(await Read(Row(Guid.NewGuid(), leg, 1)));
    Assert.NotNull(await Read(Row(dispatch, leg, 1)));

    Task<RouteDisplayCache.Snapshot?> Read(DispatchRoutePlan row) =>
      cache.GetAsync(
        dispatch,
        () => Task.FromResult<DispatchRoutePlan?>(row),
        default,
        leg
      );
  }

  [Fact]
  public void ScopeKeepsCommercialIdentityAndUsesPhysicalWorkForStorage()
  {
    var dispatch = Guid.NewGuid();
    var leg = Guid.NewGuid();
    var legacy = new PlanningScope(dispatch, null);
    var native = new PlanningScope(dispatch, leg, 7);

    Assert.Equal(dispatch, legacy.StorageKey);
    Assert.Equal(leg, native.StorageKey);
    Assert.Equal(dispatch, native.DispatchId);
    Assert.Equal(7, native.AssignmentRevision);
    Assert.NotEqual(legacy.CacheKey, native.CacheKey);
  }

  private static DispatchRoutePlan Row(Guid dispatch, Guid? leg, int version) =>
    new()
    {
      DispatchId = dispatch,
      ExecutionLegId = leg,
      PlanJson = JsonSerializer.Serialize(
        new RoutePlan
        {
          Id = Guid.NewGuid(),
          DispatchId = dispatch,
          ExecutionLegId = leg,
          Version = version,
        },
        RoutingJson.Options
      ),
    };
}
