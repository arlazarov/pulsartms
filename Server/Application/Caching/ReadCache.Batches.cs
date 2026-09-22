using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Caching;

public sealed partial class ReadCache
{
  // Families are application-defined constants, never request identifiers.
  private readonly ConcurrentDictionary<string, SemaphoreSlim[]> batchGates =
    new();

  public void InvalidateItem(string family, Guid id) =>
    Invalidate(ItemGroup(family, id));

  private string ItemGroup(string family, Guid id) =>
    $"{family}:{companies?.Id:N}:{id:N}";

  public async Task<IReadOnlyDictionary<Guid, T>> GetManyAsync<T>(
    string family,
    IReadOnlyCollection<Guid> ids,
    string key,
    Func<IReadOnlyCollection<Guid>, Task<IReadOnlyDictionary<Guid, T>>> load,
    CancellationToken ct
  )
    where T : class
  {
    ct.ThrowIfCancellationRequested();
    var result = new Dictionary<Guid, T>();
    var distinct = ids.Distinct().ToArray();
    var missing = FindMissing();
    if (missing.Count == 0)
      return result;
    var stripes = batchGates.GetOrAdd(
      family,
      _ =>
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray()
    );
    var stripe = $"{companies?.Id:N}";
    var gate = stripes[
      (uint)StringComparer.Ordinal.GetHashCode(stripe) % stripes.Length
    ];
    await gate.WaitAsync(ct);
    try
    {
      missing = FindMissing();
      if (missing.Count == 0)
        return result;
      var versions = missing.ToDictionary(
        id => id,
        id => generations.Get(ItemGroup(family, id))
      );
      var loaded = await load(missing);
      foreach (var id in missing)
      {
        var value = loaded.GetValueOrDefault(id);
        if (value is not null)
          result[id] = value;
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        var group = ItemGroup(family, id);
        if (
          json.Length <= 8 * 1024 * 1024
          && generations.Get(group) == versions[id]
        )
          bounded.Set(
            CacheKey(id, versions[id]),
            new Cached(json, null),
            new MemoryCacheEntryOptions
            {
              Size = Math.Max(1, json.Length),
              AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(
                options.Value.ReadCacheSeconds
              ),
            }
          );
      }
      return result;
    }
    finally
    {
      gate.Release();
    }

    string CacheKey(Guid id, long version) =>
      $"batch:{ItemGroup(family, id)}:{version}:{key}";

    List<Guid> FindMissing()
    {
      result.Clear();
      var absent = new List<Guid>();
      foreach (var id in distinct)
      {
        var version = generations.Get(ItemGroup(family, id));
        if (bounded.TryGetValue<Cached>(CacheKey(id, version), out var saved))
        {
          if (Restore<T?>(saved!) is { } value)
            result[id] = value;
        }
        else
          absent.Add(id);
      }
      return absent;
    }
  }
}
