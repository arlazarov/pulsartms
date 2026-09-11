using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class NextLoadDisplayCacheTests
{
  private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void ReadsDoNotExtendExpirationButValidatedReplacementDoes()
  {
    var cache = new NextLoadDisplayCache();
    var key = (Guid.NewGuid(), Guid.NewGuid());
    cache.Store(key, "first", [1], Start);
    Assert.NotNull(cache.Get(key, Start.AddMinutes(4)));
    Assert.Null(cache.Get(key, Start.AddMinutes(5)));
    cache.Store(key, "validated", [2], Start.AddMinutes(5));
    Assert.Equal("validated", cache.Get(key, Start.AddMinutes(9))?.Revision);
    Assert.Null(cache.Get(key, Start.AddMinutes(10)));
  }

  [Fact]
  public void ByteBudgetEvictsLeastRecentlyUsedAndOversizedReplacementRemovesOldSnapshot()
  {
    var cache = new NextLoadDisplayCache(maxEntries: 3, maxBytes: 10);
    var a = (Guid.NewGuid(), Guid.NewGuid());
    var b = (Guid.NewGuid(), Guid.NewGuid());
    var c = (Guid.NewGuid(), Guid.NewGuid());
    cache.Store(a, "a", new byte[4], Start);
    cache.Store(b, "b", new byte[4], Start);
    Assert.NotNull(cache.Get(a, Start));
    cache.Store(c, "c", new byte[4], Start);
    Assert.Null(cache.Get(b, Start));
    Assert.NotNull(cache.Get(a, Start));
    Assert.NotNull(cache.Get(c, Start));
    cache.Store(a, "oversized", new byte[11], Start);
    Assert.Null(cache.Get(a, Start));
    Assert.NotNull(cache.Get(c, Start));
  }

  [Fact]
  public void EntryLimitKeepsDispatchIdentitiesSeparateAndClearReleasesAllSnapshots()
  {
    var cache = new NextLoadDisplayCache(maxEntries: 2);
    var truck = Guid.NewGuid();
    var a = (truck, Guid.NewGuid());
    var b = (truck, Guid.NewGuid());
    var c = (Guid.NewGuid(), a.Item2);
    cache.Store(a, "a", [1], Start);
    cache.Store(b, "b", [2], Start);
    Assert.NotNull(cache.Get(a, Start));
    cache.Store(c, "c", [3], Start);
    Assert.Null(cache.Get(b, Start));
    Assert.Equal("a", cache.Get(a, Start)?.Revision);
    Assert.Equal("c", cache.Get(c, Start)?.Revision);
    cache.Clear();
    Assert.Null(cache.Get(a, Start));
    Assert.Null(cache.Get(c, Start));
  }
}
