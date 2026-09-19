using Application.Features.Synchronization.Options;
using Application.Features.Dispatch.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Application.Caching;

public sealed class ReadCache(IOptions<SynchronizationOptions> options) : IReadCache, IDisposable
{
  private readonly MemoryCache bounded = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
  private sealed record Cached(byte[]? Json, Domain.Entities.Dispatch.DispatchRoutePlan? Route, DispatchBoardIndex? Board = null,
    object? Shared = null, Func<object, object>? Copy = null);
  private static Domain.Entities.Dispatch.DispatchRoutePlan Copy(Domain.Entities.Dispatch.DispatchRoutePlan value) => new()
  { Id = value.Id, DispatchId = value.DispatchId, TruckId = value.TruckId, InputHash = value.InputHash, PlanJson = value.PlanJson, CreatedAt = value.CreatedAt };
  private static T Restore<T>(Cached value) => value.Board is { } board ? (T)(object)board
    : value.Shared is { } shared ? (T)value.Copy!(shared)
    : value.Route is { } route ? (T)(object)Copy(route) : JsonSerializer.Deserialize<T>(value.Json!, Json)!;
  private readonly CacheGenerations generations = new();
  private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

  public Task<T> GetAsync<T>(string group, string key, Func<Task<T>> load, TimeSpan? lifetime = null) =>
    GetAsync(group, key, load, lifetime, null);

  // Immutable results skip the JSON round trip on every hit; `copy` returns a view callers may
  // hold or reshape (for example a new list over shared records) without touching the cached instance.
  public Task<T> GetSharedAsync<T>(string group, string key, Func<Task<T>> load, Func<T, T> copy, TimeSpan? lifetime = null) where T : class =>
    GetAsync(group, key, load, lifetime, copy);

  private async Task<T> GetAsync<T>(string group, string key, Func<Task<T>> load, TimeSpan? lifetime, Func<T, T>? copy)
  {
    var version = generations.Get(group);
    var cacheKey = $"read:{group}:{version}:{key}";
    if (bounded.TryGetValue<Cached>(cacheKey, out var saved)) return Restore<T>(saved!);
    var gate = gates[(uint)StringComparer.Ordinal.GetHashCode(cacheKey) % gates.Length];
    await gate.WaitAsync();
    try
    {
      if (bounded.TryGetValue<Cached>(cacheKey, out saved)) return Restore<T>(saved!);
      var result = await load();
      var entry = result is DispatchBoardIndex board ? new Cached(null, null, board) : result is Domain.Entities.Dispatch.DispatchRoutePlan route
        ? new Cached(null, Copy(route)) : copy is not null && result is not null
        ? new Cached(JsonSerializer.SerializeToUtf8Bytes(result, Json), null, Shared: result, Copy: value => copy((T)value)!)
        : new Cached(JsonSerializer.SerializeToUtf8Bytes(result, Json), null);
      var size = entry.Board?.EstimatedBytes ?? (entry.Route is { } snapshot ? 2L * snapshot.PlanJson.Length + 1024 : entry.Json!.LongLength);
      if (size <= 8 * 1024 * 1024 && version == generations.Get(group))
        bounded.Set(cacheKey, entry.Shared is null ? entry : entry with { Json = null }, new MemoryCacheEntryOptions
        { Size = Math.Max(1, size), AbsoluteExpirationRelativeToNow = lifetime ?? TimeSpan.FromSeconds(options.Value.ReadCacheSeconds) });
      return copy is not null && result is not null ? copy(result) : result;
    }
    finally { gate.Release(); }
  }

  public void Invalidate(string group) => generations.Invalidate(group);
  public long Generation(string group) => generations.Get(group);
  public void Dispose() => bounded.Dispose();
}
