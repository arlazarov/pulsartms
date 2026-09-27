using Application.Caching;
using Application.Interfaces;
using Application.Models;
using Domain.Models.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Integrations.Google.Places;

// Stop addresses already resolved, or recently failed, so the same address
// is not asked of Google again (audit F26). It lived in the shared memory
// cache with no bound: every distinct address of twelve hours, from the
// import, the road provider and a user endpoint. It is bounded in bytes
// here and reported with the other budgets. An address forgotten early is
// only looked up again - the resolved point is stored on the stop - so this
// never holds the only copy. The address is not carrier data: the same
// address resolves the same for every carrier.
public sealed class StopGeocodeMemory(TimeProvider clock)
  : IDisposable,
    ICacheMemorySource
{
  public static readonly TimeSpan Resolved = TimeSpan.FromHours(12);

  public sealed record Failure(string Message, DateTime RetryAfter);

  private sealed record Entry(object Value, DateTimeOffset Expires);

  private readonly MemoryCache entries = new(
    new MemoryCacheOptions
    {
      TrackStatistics = true,
      SizeLimit = CacheBudgets.Geocodes,
    }
  );

  public DateTime Now => clock.GetUtcNow().UtcDateTime;

  public ResolvedAddress? Find(string address) =>
    Read(Key(address)) as ResolvedAddress;

  public Failure? FindFailure(string address) =>
    Read(Key(address) + ":failure") as Failure;

  public void Remember(string address, ResolvedAddress resolved)
  {
    var key = Key(address);
    Write(
      key,
      resolved,
      Resolved,
      Text(key)
        + Text(resolved.Address)
        + Text(resolved.City)
        + Text(resolved.Province)
        + Text(resolved.Country)
        + Text(resolved.ZipCode)
    );
  }

  public void RememberFailure(string address, Failure failure, DateTime until)
  {
    var key = Key(address) + ":failure";
    var lifetime = until - Now;
    if (lifetime > TimeSpan.Zero)
      Write(key, failure, lifetime, Text(key) + Text(failure.Message));
  }

  private object? Read(string key)
  {
    if (!entries.TryGetValue<Entry>(key, out var entry))
      return null;
    if (entry!.Expires > clock.GetUtcNow())
      return entry.Value;
    entries.Remove(key);
    return null;
  }

  // Sized from its strings and a fixed overhead for the entry and the
  // point; when the budget is full the cache refuses the entry, and the
  // address is asked again next time.
  private void Write(string key, object value, TimeSpan lifetime, long text) =>
    entries.Set(
      key,
      new Entry(value, clock.GetUtcNow() + lifetime),
      new MemoryCacheEntryOptions
      {
        Size = 256 + text,
        AbsoluteExpirationRelativeToNow = lifetime,
      }
    );

  private static long Text(string? value) => (value?.Length ?? 0) * 2L;

  private static string Key(string address) =>
    "stop-geocode:" + address.Trim().ToUpperInvariant();

  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    var statistics = entries.GetCurrentStatistics();
    return
    [
      new(
        "stop-geocodes",
        statistics?.CurrentEntryCount,
        statistics?.CurrentEstimatedSize,
        CacheBudgets.Geocodes,
        "bytes"
      ),
    ];
  }

  public void Dispose() => entries.Dispose();
}
