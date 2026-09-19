using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Dispatch.Models;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Application.Caching;

public sealed class ReadCache(IOptions<SynchronizationOptions> options)
  : IReadCache,
    IDisposable
{
  private readonly MemoryCache bounded = new(
    new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 }
  );

  private sealed record Cached(
    byte[]? Json,
    DispatchRoutePlan? Route,
    DispatchBoardIndex? Board = null
  );

  private static DispatchRoutePlan Copy(DispatchRoutePlan value) =>
    new()
    {
      Id = value.Id,
      DispatchId = value.DispatchId,
      ExecutionLegId = value.ExecutionLegId,
      AssignmentRevision = value.AssignmentRevision,
      TruckId = value.TruckId,
      InputHash = value.InputHash,
      PlanJson = value.PlanJson,
      CreatedAt = value.CreatedAt,
    };

  private static T Restore<T>(Cached value) =>
    value.Board is { } board ? (T)(object)board
    : value.Route is { } route ? (T)(object)Copy(route)
    : JsonSerializer.Deserialize<T>(value.Json!, Json)!;

  private readonly CacheGenerations generations = new();
  private readonly SemaphoreSlim[] gates = Enumerable
    .Range(0, 64)
    .Select(_ => new SemaphoreSlim(1, 1))
    .ToArray();
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  )
  {
    ReferenceHandler = ReferenceHandler.IgnoreCycles,
  };

  // The gate is shared by 64 stripes, so a slow loader also holds unrelated
  // keys. An abandoned request must stop waiting instead of queueing behind it.
  public async Task<T> GetAsync<T>(
    string group,
    string key,
    Func<Task<T>> load,
    TimeSpan? lifetime = null,
    CancellationToken ct = default
  )
  {
    var version = generations.Get(group);
    var cacheKey = $"read:{group}:{version}:{key}";
    if (bounded.TryGetValue<Cached>(cacheKey, out var saved))
      return Restore<T>(saved!);
    var gate = gates[
      (uint)StringComparer.Ordinal.GetHashCode(cacheKey) % gates.Length
    ];
    await gate.WaitAsync(ct);
    try
    {
      if (bounded.TryGetValue<Cached>(cacheKey, out saved))
        return Restore<T>(saved!);
      var result = await load();
      var entry =
        result is DispatchBoardIndex board ? new Cached(null, null, board)
        : result is DispatchRoutePlan route ? new Cached(null, Copy(route))
        : new Cached(JsonSerializer.SerializeToUtf8Bytes(result, Json), null);
      var size =
        entry.Board?.EstimatedBytes
        ?? (
          entry.Route is { } snapshot
            ? 2L * snapshot.PlanJson.Length + 1024
            : entry.Json!.LongLength
        );
      if (size <= 8 * 1024 * 1024 && version == generations.Get(group))
        bounded.Set(
          cacheKey,
          entry,
          new MemoryCacheEntryOptions
          {
            Size = Math.Max(1, size),
            AbsoluteExpirationRelativeToNow =
              lifetime ?? TimeSpan.FromSeconds(options.Value.ReadCacheSeconds),
          }
        );
      return result;
    }
    finally
    {
      gate.Release();
    }
  }

  public void Invalidate(string group) => generations.Invalidate(group);

  public long Generation(string group) => generations.Get(group);

  public void Dispose() => bounded.Dispose();
}
