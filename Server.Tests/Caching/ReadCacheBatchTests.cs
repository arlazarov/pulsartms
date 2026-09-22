using Application.Caching;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Caching;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ReadCacheBatchTests
{
  private sealed record Value(string Text);

  [Fact]
  public async Task OverlappingPagesLoadOnlyMissingOrInvalidatedItems()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var c = Guid.NewGuid();
    var calls = new List<Guid[]>();
    Task<IReadOnlyDictionary<Guid, Value>> Load(IReadOnlyCollection<Guid> ids)
    {
      calls.Add(ids.ToArray());
      return Task.FromResult<IReadOnlyDictionary<Guid, Value>>(
        ids.ToDictionary(x => x, _ => new Value("ready"))
      );
    }
    await cache.GetManyAsync("work", [a, b], "day", Load, default);
    await cache.GetManyAsync("work", [b, c], "day", Load, default);
    cache.InvalidateItem("work", a);
    await cache.GetManyAsync("work", [a, b, c], "day", Load, default);
    cache.Invalidate("board");
    cache.Invalidate("route-previews");
    await cache.GetManyAsync("work", [a, b, c], "day", Load, default);
    Assert.Equal(3, calls.Count);
    Assert.Equal(new[] { a, b }, calls[0]);
    Assert.Equal(new[] { c }, calls[1]);
    Assert.Equal(new[] { a }, calls[2]);
    await cache.GetManyAsync("work", [a, b, c], "new-settings", Load, default);
    Assert.Equal(new[] { a, b, c }, calls[3]);
  }

  [Fact]
  public async Task ConcurrentDemandCoalescesAndLateLoadsCannotRestoreOldValues()
  {
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var id = Guid.NewGuid();
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var calls = 0;
    async Task<IReadOnlyDictionary<Guid, Value>> Load(
      IReadOnlyCollection<Guid> ids
    )
    {
      var value = ++calls;
      entered.TrySetResult();
      if (value == 1)
        await release.Task;
      return ids.ToDictionary(x => x, _ => new Value(value.ToString()));
    }
    var first = cache.GetManyAsync("work", [id], "key", Load, default);
    await entered.Task;
    var second = cache.GetManyAsync("work", [id], "key", Load, default);
    cache.InvalidateItem("work", id);
    release.SetResult();
    await first;
    Assert.Equal("2", (await second)[id].Text);
    Assert.Equal(
      "2",
      (await cache.GetManyAsync("work", [id], "key", Load, default))[id].Text
    );
    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task ItemInvalidationIsCompanyScopedAndMissingRowsAreCached()
  {
    var company = new TestCompany();
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions()),
      company
    );
    var owner = Guid.NewGuid();
    var other = Guid.NewGuid();
    var id = Guid.NewGuid();
    var calls = 0;
    Task<IReadOnlyDictionary<Guid, Value>> Load(IReadOnlyCollection<Guid> ids)
    {
      calls++;
      return Task.FromResult<IReadOnlyDictionary<Guid, Value>>(
        new Dictionary<Guid, Value>()
      );
    }
    using (company.As(owner))
      await cache.GetManyAsync("work", [id], "key", Load, default);
    using (company.As(other))
      await cache.GetManyAsync("work", [id], "key", Load, default);
    using (company.As(owner))
      cache.InvalidateItem("work", id);
    using (company.As(other))
      Assert.Empty(
        await cache.GetManyAsync("work", [id], "key", Load, default)
      );
    Assert.Equal(2, calls);
    using (company.As(owner))
      await cache.GetManyAsync("work", [id], "key", Load, default);
    Assert.Equal(3, calls);
  }
}
