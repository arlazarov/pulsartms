using Application.Caching;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ReadCacheSharedTests
{
  private sealed record Station(Guid Id, string Name);

  [Fact]
  public async Task SharedValuesReturnCallerCopiesOverTheSameRecordsWithoutReloading()
  {
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var loads = 0;
    Task<List<Station>> Load() { loads++; return Task.FromResult(new List<Station> { new(Guid.NewGuid(), "A"), new(Guid.NewGuid(), "B") }); }
    var first = await cache.GetSharedAsync("fuel", "today", Load, stations => new(stations));
    var second = await cache.GetSharedAsync("fuel", "today", Load, stations => new(stations));
    Assert.Equal(1, loads);
    Assert.NotSame(first, second);
    Assert.Same(first[0], second[0]);
    first.Add(new(Guid.NewGuid(), "C"));
    Assert.Equal(2, (await cache.GetSharedAsync("fuel", "today", Load, stations => new(stations))).Count);
    cache.Invalidate("fuel");
    await cache.GetSharedAsync("fuel", "today", Load, stations => new(stations));
    Assert.Equal(2, loads);
  }

  [Fact]
  public async Task SerializedValuesStillRoundTripThroughJson()
  {
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var loaded = new List<Station> { new(Guid.NewGuid(), "A") };
    var first = await cache.GetAsync("fuel", "copy", () => Task.FromResult(loaded));
    var second = await cache.GetAsync("fuel", "copy", () => Task.FromResult(loaded));
    Assert.Same(loaded, first);
    Assert.NotSame(loaded, second);
    Assert.Equal(loaded, second);
  }

  [Fact]
  public async Task OversizedSharedValuesAreNotRetained()
  {
    using var cache = new ReadCache(Options.Create(new SynchronizationOptions()));
    var loads = 0;
    Task<List<Station>> Load() { loads++; return Task.FromResult(Enumerable.Range(0, 60_000).Select(i => new Station(Guid.NewGuid(), new string('x', 120))).ToList()); }
    await cache.GetSharedAsync("fuel", "huge", Load, stations => new(stations));
    await cache.GetSharedAsync("fuel", "huge", Load, stations => new(stations));
    Assert.Equal(2, loads);
  }
}
