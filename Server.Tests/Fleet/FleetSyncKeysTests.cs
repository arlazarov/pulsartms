using Application.Features.Fleet.Services;
using Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Fleet;

// The fleet and load imports run once per carrier over one process-wide
// cache. Their keys were constants, so a second carrier's run read the
// first one's driver ids and "nothing changed" signatures.
[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetSyncKeysTests
{
  private static readonly Guid Other = Guid.NewGuid();

  [Fact]
  public void EachCarrierHasItsOwnKeysAndForgetsOnlyItsOwn()
  {
    using var cache = new MemoryCache(new MemoryCacheOptions());
    Assert.NotEqual(
      FleetSyncKeys.DriverIds(Company.Amf),
      FleetSyncKeys.DriverIds(Other)
    );
    Assert.NotEqual(
      FleetSyncKeys.AssignmentSignature(Company.Amf),
      FleetSyncKeys.AssignmentSignature(Other)
    );
    Assert.NotEqual(
      FleetSyncKeys.DispatchSnapshot(Company.Amf, "p"),
      FleetSyncKeys.DispatchSnapshot(Other, "p")
    );
    cache.Set(FleetSyncKeys.DriverIds(Company.Amf), new[] { "amf" });
    cache.Set(FleetSyncKeys.DriverIds(Other), new[] { "other" });
    cache.Set(FleetSyncKeys.AssignmentSignature(Other), "other");

    FleetSyncKeys.Forget(cache, Company.Amf);

    Assert.False(
      cache.TryGetValue(FleetSyncKeys.DriverIds(Company.Amf), out _)
    );
    Assert.Equal(
      ["other"],
      cache.Get<string[]>(FleetSyncKeys.DriverIds(Other))!
    );
    Assert.Equal(
      "other",
      cache.Get<string>(FleetSyncKeys.AssignmentSignature(Other))
    );
  }
}
