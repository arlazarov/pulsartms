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
//
// A failure is kept apart from resolved addresses, in its own part of the
// budget, so no number of resolved addresses can push it out before its
// retry (Root's review). If failures alone fill their part, a new one may
// not be kept; then the address can be asked again before its retry, and
// what bounds that is the attempt limit: at most AttemptsPerMinute calls
// to Google in any minute, whatever is or is not remembered.
// failureBudget: the part of the budget for failures; a test sets it below
// one entry to make every failure unkeepable, as a full memory would.
public sealed class StopGeocodeMemory(
  TimeProvider clock,
  long failureBudget = StopGeocodeMemory.FailureBudget
) : IDisposable, ICacheMemorySource
{
  public static readonly TimeSpan Resolved = TimeSpan.FromHours(12);
  public const int AttemptsPerMinute = 60;
  public const long FailureBudget = CacheBudgets.Geocodes / 8;

  public sealed record Failure(string Message, DateTime RetryAfter);

  private sealed record Entry(object Value, DateTimeOffset Expires);

  private readonly MemoryCache entries = new(
    new MemoryCacheOptions
    {
      TrackStatistics = true,
      SizeLimit = CacheBudgets.Geocodes - failureBudget,
    }
  );
  private readonly MemoryCache failures = new(
    new MemoryCacheOptions { TrackStatistics = true, SizeLimit = failureBudget }
  );
  private readonly object attemptsGate = new();
  private readonly Queue<DateTimeOffset> attempts = new();

  public DateTime Now => clock.GetUtcNow().UtcDateTime;

  public ResolvedAddress? Find(string address) =>
    Read(entries, Key(address)) as ResolvedAddress;

  public Failure? FindFailure(string address) =>
    Read(failures, Key(address)) as Failure;

  // Whether one more call to Google fits in the last minute; when not, the
  // time the oldest of them leaves it.
  public bool TryAttempt(out DateTime retryAfter)
  {
    var now = clock.GetUtcNow();
    lock (attemptsGate)
    {
      while (attempts.Count > 0 && attempts.Peek() <= now.AddMinutes(-1))
        attempts.Dequeue();
      if (attempts.Count >= AttemptsPerMinute)
      {
        retryAfter = attempts.Peek().AddMinutes(1).UtcDateTime;
        return false;
      }
      attempts.Enqueue(now);
      retryAfter = default;
      return true;
    }
  }

  public void Remember(string address, ResolvedAddress resolved)
  {
    var key = Key(address);
    Write(
      entries,
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
    var key = Key(address);
    var lifetime = until - Now;
    if (lifetime > TimeSpan.Zero)
      Write(
        failures,
        key,
        failure,
        lifetime,
        Text(key) + Text(failure.Message)
      );
  }

  private object? Read(MemoryCache cache, string key)
  {
    if (!cache.TryGetValue<Entry>(key, out var entry))
      return null;
    if (entry!.Expires > clock.GetUtcNow())
      return entry.Value;
    cache.Remove(key);
    return null;
  }

  // Sized from its strings and a fixed overhead for the entry and the
  // point; when the budget is full the cache refuses the entry, and the
  // address is asked again next time.
  private void Write(
    MemoryCache cache,
    string key,
    object value,
    TimeSpan lifetime,
    long text
  ) =>
    cache.Set(
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
    var resolved = entries.GetCurrentStatistics();
    var failed = failures.GetCurrentStatistics();
    return
    [
      new(
        "stop-geocodes",
        resolved?.CurrentEntryCount,
        resolved?.CurrentEstimatedSize,
        CacheBudgets.Geocodes - failureBudget,
        "bytes"
      ),
      new(
        "stop-geocode-failures",
        failed?.CurrentEntryCount,
        failed?.CurrentEstimatedSize,
        failureBudget,
        "bytes"
      ),
    ];
  }

  public void Dispose()
  {
    entries.Dispose();
    failures.Dispose();
  }
}
