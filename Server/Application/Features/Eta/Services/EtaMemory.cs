using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Application.Models;
using Domain.Models.Eta;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Eta.Services;

public sealed class EtaMemory(TimeProvider? clock = null)
  : IDisposable,
    ICacheMemorySource
{
  // How long a forecast holds, and the longest the worker sleeps between
  // checks. A forecast is due the moment it expires, not a whole interval
  // later: the worker wakes at the earliest expiry, at this interval, or at
  // once when a route, stop, assignment or duty change asks for it.
  public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

  private readonly TimeProvider time = clock ?? TimeProvider.System;

  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    var stats0 = futureTimings.GetCurrentStatistics();
    return
    [
      new(
        "eta-future-timings",
        stats0?.CurrentEntryCount,
        stats0?.CurrentEstimatedSize,
        32768,
        "units"
      ),
      new("eta-current", Results.Count, null, null, "unmeasured"),
      new("eta-timing", Timing.Count, Timing.RetainedUnits, 32768, "units"),
    ];
  }

  public sealed record ScopeIdentity(Guid DispatchId, Guid? ExecutionLegId);

  public sealed record Entry(
    string Signature,
    DispatchEta Value,
    string? RouteKey = null
  )
  {
    public string? ChainInputHash { get; init; }

    // The work this forecast is for: the load, its leg, the assignment and
    // its stops. A forecast for the same work stays readable while it is
    // replaced; one for other work never is.
    public string? WorkKey { get; init; }

    // Its road moved on - a new version, a passed stop, a confirmed
    // deviation - so it is due again, but it is still this work's forecast
    // and is shown, marked as updating, until the new one lands.
    public bool Superseded { get; init; }

    // Whose hours it was calculated with, so a duty change can make it due.
    public string? Driver { get; init; }

    // The saved road it was calculated on, to order two results of it.
    public Guid? PlanId { get; init; }
    public int? PlanVersion { get; init; }
  }

  // A calculation reads its road before it waits for the dispatch's gate,
  // so one that started on an older version of the same road can finish
  // after one on a newer version. It never replaces it: the newer forecast
  // stays and the late one is dropped. Results for other roads or other
  // work cannot be ordered this way and replace as before; readers already
  // refuse to show them as current.
  public bool Publish(Guid key, Entry entry)
  {
    var kept = Results.AddOrUpdate(
      key,
      entry,
      (_, existing) => Older(entry, existing) ? existing : entry
    );
    return ReferenceEquals(kept, entry);
  }

  private static bool Older(Entry candidate, Entry existing) =>
    candidate.WorkKey is not null
    && candidate.WorkKey == existing.WorkKey
    && candidate.PlanId is not null
    && candidate.PlanId == existing.PlanId
    && candidate.PlanVersion < existing.PlanVersion;

  public readonly ConcurrentDictionary<Guid, Entry> Results = new();
  public readonly ConcurrentDictionary<Guid, DateTime> Viewed = new();
  private readonly ConcurrentDictionary<Guid, string> demandedInputs = new();
  private readonly ConcurrentDictionary<Guid, ScopeIdentity> scopes = new();
  public EtaRouteTimingCache Timing { get; } = new();
  private readonly MemoryCache futureTimings = new(
    new MemoryCacheOptions { TrackStatistics = true, SizeLimit = 32768 }
  );
  private readonly SemaphoreSlim[] futureGates = Enumerable
    .Range(0, 16)
    .Select(_ => new SemaphoreSlim(1))
    .ToArray();
  private readonly Channel<bool> refresh = Channel.CreateBounded<bool>(
    new BoundedChannelOptions(1)
    {
      SingleReader = true,
      FullMode = BoundedChannelFullMode.DropWrite,
    }
  );
  private readonly SemaphoreSlim[] gates = Enumerable
    .Range(0, 32)
    .Select(_ => new SemaphoreSlim(1))
    .ToArray();

  public SemaphoreSlim Gate(Guid id) =>
    gates[(uint)id.GetHashCode() % gates.Length];

  public Guid Scope(Guid dispatchId, Guid? executionLegId)
  {
    var key = executionLegId ?? dispatchId;
    if (executionLegId.HasValue)
      scopes[key] = new(dispatchId, executionLegId);
    return key;
  }

  public ScopeIdentity Resolve(Guid key) =>
    scopes.GetValueOrDefault(key) ?? new(key, null);

  public void View(Guid id, DateTime now)
  {
    var firstView = Viewed.TryAdd(id, now);
    if (!firstView)
      Viewed[id] = now;
    if (firstView)
      RequestRefresh();
  }

  public void RequestRefresh() => refresh.Writer.TryWrite(true);

  public bool RemoveIfCurrent(Guid dispatchId, Entry expected) =>
    ((ICollection<KeyValuePair<Guid, Entry>>)Results).Remove(
      new(dispatchId, expected)
    );

  public bool SupersedeIfCurrent(Guid dispatchId, Entry expected) =>
    expected.Superseded
    || Results.TryUpdate(
      dispatchId,
      expected with
      {
        Superseded = true,
      },
      expected
    );

  // A driver went on or off duty, or was read for the first time: what
  // their forecasts assumed about hours no longer holds. Only those are
  // made due - the rest of the fleet is left as it is.
  public void DutyChanged(IReadOnlyCollection<string> drivers)
  {
    if (drivers.Count == 0)
      return;
    var changed = false;
    foreach (var (key, entry) in Results)
      if (entry.Driver is { } driver && drivers.Contains(driver))
        changed |= SupersedeIfCurrent(key, entry);
    if (changed)
      RequestRefresh();
  }

  public void Forget(Guid dispatchId)
  {
    Viewed.TryRemove(dispatchId, out _);
    Results.TryRemove(dispatchId, out _);
    demandedInputs.TryRemove(dispatchId, out _);
    scopes.TryRemove(dispatchId, out _);
  }

  public void Demand(Guid rootDispatchId, string inputHash, DateTime now)
  {
    View(rootDispatchId, now);
    while (true)
    {
      if (demandedInputs.TryGetValue(rootDispatchId, out var previous))
      {
        if (previous == inputHash)
        {
          if (
            Results.TryGetValue(rootDispatchId, out var invalid)
            && invalid.ChainInputHash != inputHash
            && RemoveIfCurrent(rootDispatchId, invalid)
          )
            RequestRefresh();
          return;
        }
        if (!demandedInputs.TryUpdate(rootDispatchId, inputHash, previous))
          continue;
      }
      else if (!demandedInputs.TryAdd(rootDispatchId, inputHash))
        continue;
      if (
        Results.TryGetValue(rootDispatchId, out var cached)
        && cached.ChainInputHash != inputHash
      )
        RemoveIfCurrent(rootDispatchId, cached);
      RequestRefresh();
      return;
    }
  }

  public async Task<ImmutableArray<EtaFutureTiming>> FutureTimingAsync(
    string revision,
    Func<Task<ImmutableArray<EtaFutureTiming>>> compile,
    CancellationToken ct
  )
  {
    if (
      futureTimings.TryGetValue<ImmutableArray<EtaFutureTiming>>(
        revision,
        out var ready
      )
    )
      return ready;
    var gate = futureGates[
      (uint)StringComparer.Ordinal.GetHashCode(revision) % futureGates.Length
    ];
    await gate.WaitAsync(ct);
    try
    {
      if (
        futureTimings.TryGetValue<ImmutableArray<EtaFutureTiming>>(
          revision,
          out ready
        )
      )
        return ready;
      var value = await compile();
      ct.ThrowIfCancellationRequested();
      var size =
        1L
        + value.Sum(x =>
          1L
          + x.StopPoints.Length
          + (x.Connection?.RetainedUnits ?? 0)
          + (x.Route?.RetainedUnits ?? 0)
        );
      if (size <= 32768)
        futureTimings.Set(
          revision,
          value,
          new MemoryCacheEntryOptions
          {
            Size = size,
            SlidingExpiration = TimeSpan.FromMinutes(30),
          }
        );
      return value;
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task WaitForRefreshAsync(CancellationToken ct)
  {
    var now = time.GetUtcNow().UtcDateTime;
    var delay = NextCheck(now) - now;
    if (delay <= TimeSpan.Zero)
      return;
    using var timeout = new CancellationTokenSource(delay, time);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
      ct,
      timeout.Token
    );
    try
    {
      await refresh.Reader.ReadAsync(linked.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
  }

  // When the worker next has something to look at without being asked: the
  // earliest forecast still to expire, or a full interval from now. Missing,
  // superseded and already expired forecasts are not counted here - the
  // event that caused them wakes the worker, and a refresh that failed is
  // retried at the interval rather than in a loop.
  public DateTime NextCheck(DateTime now)
  {
    var next = now + RefreshInterval;
    foreach (var item in Viewed)
      if (
        Results.TryGetValue(item.Key, out var result)
        && !result.Superseded
        && result.Value.ValidUntil > now
        && result.Value.ValidUntil < next
      )
        next = result.Value.ValidUntil;
    return next;
  }

  public IEnumerable<Guid> Due(DateTime now)
  {
    foreach (var item in Viewed)
    {
      if (item.Value < now.AddMinutes(-10))
      {
        Forget(item.Key);
        continue;
      }
      if (
        !Results.TryGetValue(item.Key, out var result)
        || result.Superseded
        || result.Value.ValidUntil <= now
      )
        yield return item.Key;
    }
  }

  public void Dispose()
  {
    futureTimings.Dispose();
    foreach (var gate in futureGates)
      gate.Dispose();
    foreach (var gate in gates)
      gate.Dispose();
  }
}
