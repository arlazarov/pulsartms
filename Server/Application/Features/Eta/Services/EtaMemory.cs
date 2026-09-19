using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Application.Features.Eta.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Eta.Services;

public sealed class EtaMemory : IDisposable
{
  public sealed record ScopeIdentity(Guid DispatchId, Guid? ExecutionLegId);

  public sealed record Entry(
    string Signature,
    DispatchEta Value,
    string? RouteKey = null
  )
  {
    public string? ChainInputHash { get; init; }
  }

  public readonly ConcurrentDictionary<Guid, Entry> Results = new();
  public readonly ConcurrentDictionary<Guid, DateTime> Viewed = new();
  private readonly ConcurrentDictionary<Guid, string> demandedInputs = new();
  private readonly ConcurrentDictionary<Guid, ScopeIdentity> scopes = new();
  public EtaRouteTimingCache Timing { get; } = new();
  private readonly MemoryCache futureTimings = new(
    new MemoryCacheOptions { SizeLimit = 32768 }
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
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    try
    {
      await refresh.Reader.ReadAsync(timeout.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
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
