using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Application.Diagnostics;
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
      new("eta-current", touched.Count, null, MaximumScopes, "scopes"),
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

    // A saved forecast read back (EtaService.Saved): its keys are the
    // hashes the store keeps, not the keys themselves.
    public bool KeysHashed { get; init; }
  }

  // A calculation reads its road before it waits for the dispatch's gate,
  // so one that started on an older version of the same road can finish
  // after one on a newer version. It never replaces it: the newer forecast
  // stays and the late one is dropped. Two results of one version are
  // ordered as the forecast store orders them, since each is published only
  // after the store accepted it: the store keeps a row only for a strictly
  // later CalculatedAt, so an earlier or equal one never replaces the one
  // kept here either. Results for other roads or other
  // work cannot be ordered this way and replace as before; readers already
  // refuse to show them as current.
  // A scope is written with its identity: a leg's scope names its load in
  // the same step as the write, so the bound can never leave a leg's
  // forecast, view or demand without the identity the refresh worker
  // resolves it by (Root's review of b1401ddb). The Guid overloads are a
  // load's own scope, for tests and the load itself.
  public bool Publish(ScopeIdentity scope, Entry entry)
  {
    var key = Key(scope);
    Entry kept;
    lock (Lifecycle(key))
    {
      ScopeLocked(scope.DispatchId, scope.ExecutionLegId);
      Touch(key);
      kept = Results.AddOrUpdate(
        key,
        entry,
        (_, existing) => Older(entry, existing) ? existing : entry
      );
    }
    Bound();
    return ReferenceEquals(kept, entry);
  }

  internal bool Publish(Guid key, Entry entry) =>
    Publish(new ScopeIdentity(key, null), entry);

  public static Guid Key(ScopeIdentity scope) =>
    scope.ExecutionLegId ?? scope.DispatchId;

  private static bool Older(Entry candidate, Entry existing) =>
    candidate.WorkKey is not null
    && candidate.WorkKey == existing.WorkKey
    && candidate.PlanId is not null
    && candidate.PlanId == existing.PlanId
    && (
      candidate.PlanVersion < existing.PlanVersion
      || candidate.PlanVersion == existing.PlanVersion
        && candidate.Value.CalculatedAt <= existing.Value.CalculatedAt
    );

  public readonly ConcurrentDictionary<Guid, Entry> Results = new();

  // Every scope this memory holds anything for - a forecast, an identity,
  // a demand, an answer - with when it was last written or read. A scope
  // untouched for ten minutes is forgotten in every map at once (Due), and
  // the number of scopes is bounded whatever roles this process runs: past
  // the bound the least recently touched are forgotten, down to three
  // quarters of it, so the sort runs once per quarter of new scopes. A
  // forgotten forecast costs a display read one saved-forecast read; the
  // store is the truth (stage 4e).
  //
  // A scope's touch and the write it stands for happen under the scope's
  // lifecycle lock, and forgetting a scope takes the same lock and drops
  // every map together - the forecast, the leg's identity, the view, the
  // answers and the touch - so no write is left untracked and no identity
  // outlives or predeceases the rest. Due and the bound decide from a
  // snapshot, then forget only if the scope is, under its lock, still idle
  // or still untouched since that snapshot: a scope refreshed meanwhile is
  // kept.
  public const int MaximumScopes = 1024;

  private readonly record struct Touched(DateTime At, long Sequence);

  private readonly ConcurrentDictionary<Guid, Touched> touched = new();
  private readonly object[] lifecycle = Enumerable
    .Range(0, 64)
    .Select(_ => new object())
    .ToArray();
  private long touches;
  private int trimming;
  private long trims;

  // How often the bound had to trim, for tests that count the work.
  internal long Trims => Interlocked.Read(ref trims);

  internal bool Tracks(Guid key) => touched.ContainsKey(key);

  // Seams for tests that interleave a write with a forget
  // deterministically: after a touch, still holding the scope's lock; and
  // before a forget takes it.
  internal Action<Guid>? AfterTouch { get; set; }
  internal Action<Guid>? BeforeForget { get; set; }

  private object Lifecycle(Guid key) =>
    lifecycle[(uint)key.GetHashCode() % lifecycle.Length];

  // Held under the scope's lifecycle lock, with the write it stands for.
  private void Touch(Guid key)
  {
    touched[key] = new(
      time.GetUtcNow().UtcDateTime,
      Interlocked.Increment(ref touches)
    );
    AfterTouch?.Invoke(key);
  }

  // Outside any lifecycle lock, after a write.
  private void Bound()
  {
    if (touched.Count > MaximumScopes)
      Trim();
  }

  private void Trim()
  {
    if (Interlocked.Exchange(ref trimming, 1) == 1)
      return;
    try
    {
      var excess = touched.Count - MaximumScopes * 3 / 4;
      if (touched.Count <= MaximumScopes || excess <= 0)
        return;
      Interlocked.Increment(ref trims);
      foreach (
        var (key, seen) in touched
          .OrderBy(x => x.Value.At)
          .ThenBy(x => x.Value.Sequence)
          .Take(excess)
          .ToArray()
      )
        ForgetUntouchedSince(key, seen.Sequence);
    }
    finally
    {
      Volatile.Write(ref trimming, 0);
    }
  }

  private void ForgetUntouchedSince(Guid key, long sequence)
  {
    BeforeForget?.Invoke(key);
    lock (Lifecycle(key))
      if (!touched.TryGetValue(key, out var now) || now.Sequence == sequence)
        ForgetLocked(key);
  }

  private void ForgetIfIdle(Guid key, DateTime idle)
  {
    BeforeForget?.Invoke(key);
    lock (Lifecycle(key))
    {
      var last =
        Viewed.TryGetValue(key, out var viewed) ? viewed
        : touched.TryGetValue(key, out var at) ? at.At
        : DateTime.MinValue;
      if (last < idle)
        ForgetLocked(key);
    }
  }

  public readonly ConcurrentDictionary<Guid, DateTime> Viewed = new();
  private readonly ConcurrentDictionary<Guid, string> demandedInputs = new();

  // What the last map read of each forecast scope answered (current,
  // updating, other work and the parts that differed, or no entry), and
  // what the planning summary last published about it. Kept so a summary
  // published without an ETA can say why, once per change.
  private readonly ConcurrentDictionary<Guid, string> mapAnswers = new();
  private readonly ConcurrentDictionary<Guid, string> publishedAnswers = new();

  public void NoteMapAnswer(ScopeIdentity scope, string answer)
  {
    var key = Key(scope);
    lock (Lifecycle(key))
    {
      ScopeLocked(scope.DispatchId, scope.ExecutionLegId);
      Touch(key);
      mapAnswers[key] = answer;
    }
    Bound();
  }

  internal void NoteMapAnswer(Guid key, string answer) =>
    NoteMapAnswer(new ScopeIdentity(key, null), answer);

  public string? MapAnswer(Guid key) => mapAnswers.GetValueOrDefault(key);

  // What a planning summary about to be published says of its ETA -
  // shown, or why not - when that differs from the last one for the same
  // work; null when it is the same, so a repeat is not logged.
  public string? SummaryAnswerChange(
    Guid dispatchId,
    Guid? executionLegId,
    bool hasEta
  )
  {
    var key = executionLegId ?? dispatchId;
    string? change;
    lock (Lifecycle(key))
    {
      ScopeLocked(dispatchId, executionLegId);
      Touch(key);
      var answer = hasEta ? "shown" : MapAnswer(key) ?? "not-read";
      var previous = publishedAnswers.GetValueOrDefault(key);
      publishedAnswers[key] = answer;
      change = previous == answer ? null : answer;
    }
    Bound();
    return change;
  }

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

  internal Guid Scope(Guid dispatchId, Guid? executionLegId)
  {
    var key = executionLegId ?? dispatchId;
    if (!executionLegId.HasValue)
      return key;
    lock (Lifecycle(key))
      ScopeLocked(dispatchId, executionLegId);
    Bound();
    return key;
  }

  private void ScopeLocked(Guid dispatchId, Guid? executionLegId)
  {
    if (executionLegId is not { } leg)
      return;
    Touch(leg);
    scopes[leg] = new(dispatchId, executionLegId);
  }

  public ScopeIdentity Resolve(Guid key) =>
    scopes.GetValueOrDefault(key) ?? new(key, null);

  public void View(ScopeIdentity scope, DateTime now)
  {
    var key = Key(scope);
    bool firstView;
    lock (Lifecycle(key))
    {
      ScopeLocked(scope.DispatchId, scope.ExecutionLegId);
      firstView = ViewLocked(key, now);
    }
    Bound();
    if (firstView)
      RequestRefresh();
  }

  internal void View(Guid id, DateTime now) =>
    View(new ScopeIdentity(id, null), now);

  private bool ViewLocked(Guid id, DateTime now)
  {
    Touch(id);
    var firstView = Viewed.TryAdd(id, now);
    if (!firstView)
      Viewed[id] = now;
    return firstView;
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

  // True when a forecast readers could see was dropped.
  public bool Forget(Guid dispatchId)
  {
    BeforeForget?.Invoke(dispatchId);
    lock (Lifecycle(dispatchId))
      return ForgetLocked(dispatchId);
  }

  private bool ForgetLocked(Guid dispatchId)
  {
    Viewed.TryRemove(dispatchId, out _);
    var dropped = Results.TryRemove(dispatchId, out _);
    demandedInputs.TryRemove(dispatchId, out _);
    scopes.TryRemove(dispatchId, out _);
    mapAnswers.TryRemove(dispatchId, out _);
    publishedAnswers.TryRemove(dispatchId, out _);
    touched.TryRemove(dispatchId, out _);
    return dropped;
  }

  public void Demand(ScopeIdentity scope, string inputHash, DateTime now)
  {
    var key = Key(scope);
    bool firstView;
    lock (Lifecycle(key))
    {
      ScopeLocked(scope.DispatchId, scope.ExecutionLegId);
      firstView = ViewLocked(key, now);
      DemandLocked(key, inputHash);
    }
    Bound();
    if (firstView)
      RequestRefresh();
  }

  internal void Demand(Guid rootDispatchId, string inputHash, DateTime now) =>
    Demand(new ScopeIdentity(rootDispatchId, null), inputHash, now);

  private void DemandLocked(Guid rootDispatchId, string inputHash)
  {
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
          {
            DemandRemoved();
            RequestRefresh();
          }
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
        && RemoveIfCurrent(rootDispatchId, cached)
      )
        DemandRemoved();
      RequestRefresh();
      return;
    }
  }

  // A reader's chain description differed from the one the forecast was
  // published with, so the map loses it until the worker publishes again;
  // counted for `GET /api/diagnostics/stages`.
  private static void DemandRemoved() =>
    PerformanceStages.Count("eta-memory", "demand-removed", 1);

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
    // A scope not viewed, or not touched at all, for ten minutes is
    // forgotten in every map - not only the ones a view created.
    var idle = now.AddMinutes(-10);
    foreach (var (key, at) in touched)
      if ((Viewed.TryGetValue(key, out var viewed) ? viewed : at.At) < idle)
        ForgetIfIdle(key, idle);
    foreach (var item in Viewed)
    {
      if (item.Value < idle)
      {
        ForgetIfIdle(item.Key, idle);
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
