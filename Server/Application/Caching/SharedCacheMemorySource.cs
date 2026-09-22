using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Caching;

public sealed class SharedCacheMemorySource(IMemoryCache cache)
  : ICacheMemorySource
{
  public IReadOnlyList<CacheMemorySnapshot> ReadMemory() =>
    [
      new(
        "shared",
        cache.GetCurrentStatistics()?.CurrentEntryCount,
        null,
        null,
        "unmeasured"
      ),
    ];
}
