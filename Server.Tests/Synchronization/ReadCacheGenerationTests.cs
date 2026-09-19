using Application.Caching;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ReadCacheGenerationTests
{
  [Fact]
  public async Task ForgottenGroupsCannotReviveOldCachedValues()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var calls = 0;
    Task<int> Load() => Task.FromResult(++calls);
    Assert.Equal(1, await cache.GetAsync("old", "value", Load));
    cache.Invalidate("old");
    Assert.Equal(2, await cache.GetAsync("old", "value", Load));
    var version = cache.Generation("old");
    for (var i = 0; i < 5000; i++)
      cache.Generation($"route:{i}");
    Assert.True(cache.Generation("old") > version);
    Assert.Equal(3, await cache.GetAsync("old", "value", Load));
  }

  [Fact]
  public async Task GenerationEvictionDuringLoadCannotPublishIntoANewerGeneration()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var response = new TaskCompletionSource<int>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var old = cache.GetAsync("old", "value", () => response.Task);
    for (var i = 0; i < 5000; i++)
      cache.Generation($"route:{i}");
    var latest = cache.GetAsync("old", "value", () => Task.FromResult(2));
    response.SetResult(1);
    Assert.Equal(1, await old);
    Assert.Equal(2, await latest);
    Assert.Equal(
      2,
      await cache.GetAsync("old", "value", () => Task.FromResult(3))
    );
  }

  [Fact]
  public void RecentlyUsedGroupsKeepTheirIndependentGeneration()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    cache.Invalidate("active");
    var version = cache.Generation("active");
    for (var i = 0; i < 9000; i++)
    {
      cache.Generation($"route:{i}");
      Assert.Equal(version, cache.Generation("active"));
    }
    cache.Invalidate("active");
    Assert.True(cache.Generation("active") > version);
  }
}
