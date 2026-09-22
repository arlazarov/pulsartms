using Application.Caching;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Options;

namespace Server.Tests.Synchronization;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public class ReadCacheMemoryTests
{
  [Fact]
  public async Task RouteSnapshotsShareImmutableJsonButNotMutableEntities()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var json = new string('x', 1_000_000);
    var entity = new DispatchRoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 42,
      PlanJson = json,
      InputHash = "original",
      GeometryChunks =
      [
        new()
        {
          CompanyId = Guid.NewGuid(),
          Key = "immutable",
          CoordinatesJson = "[40,-80]",
        },
      ],
    };
    var calls = 0;
    Task<DispatchRoutePlan> Load()
    {
      calls++;
      return Task.FromResult(entity);
    }
    await cache.GetAsync("route", "one", Load);
    var copy = await cache.GetAsync("route", "one", Load);
    Assert.NotSame(entity, copy);
    Assert.Same(json, copy.PlanJson);
    var legId = entity.ExecutionLegId;
    Assert.Equal(legId, copy.ExecutionLegId);
    Assert.Equal(42, copy.AssignmentRevision);
    var chunkCompany = entity.GeometryChunks[0].CompanyId;
    copy.GeometryChunks[0].CompanyId = Guid.NewGuid();
    entity.GeometryChunks[0].CompanyId = Guid.NewGuid();
    var chunkCopy = await cache.GetAsync("route", "one", Load);
    Assert.Equal(chunkCompany, chunkCopy.GeometryChunks[0].CompanyId);
    Assert.Same(
      entity.GeometryChunks[0].CoordinatesJson,
      chunkCopy.GeometryChunks[0].CoordinatesJson
    );
    copy.PlanJson = "changed";
    entity.InputHash = "changed";
    copy.ExecutionLegId = null;
    copy.AssignmentRevision = 100;
    var again = await cache.GetAsync("route", "one", Load);
    Assert.Equal("original", again.InputHash);
    Assert.Same(json, again.PlanJson);
    Assert.Equal(legId, again.ExecutionLegId);
    Assert.Equal(42, again.AssignmentRevision);
    Assert.Equal(1, calls);
    cache.Invalidate("route");
    await cache.GetAsync("route", "one", Load);
    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task OversizedRoutesAreNotRetained()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var entity = new DispatchRoutePlan
    {
      PlanJson = new string('x', 4_194_304),
    };
    var calls = 0;
    Task<DispatchRoutePlan> Load()
    {
      calls++;
      return Task.FromResult(entity);
    }
    await cache.GetAsync("route", "large", Load);
    await cache.GetAsync("route", "large", Load);
    Assert.Equal(2, calls);
  }
}
