using Application.Caching;
using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Services;
using Domain.Entities.Dispatch;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Routing.Services.Routes;

public sealed class RouteDisplayCache(ReadCache reads) : IDisposable
{
  private const long Capacity = 32 * 1024 * 1024;
  private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = Capacity });
  private readonly KeyedGates gates = new();
  private readonly SemaphoreSlim coldLoads = new(2, 2);
  private sealed record Cached(long Generation, Snapshot Value);

  public sealed class Snapshot
  {
    private readonly DispatchRoutePlan metadata;
    private readonly byte[] displayJson;
    private readonly byte[] metadataJson;
    private readonly Guid planId;
    private readonly int planVersion;
    public DispatchRoutePlan Metadata => Copy(metadata);
    public RouteGeometry Geometry { get; }
    public long Size { get; }
    public int DisplayBytes => displayJson.Length;
    public int MetadataBytes => metadataJson.Length;

    internal Snapshot(DispatchRoutePlan metadata, byte[] displayJson, byte[] metadataJson,
      RouteGeometry geometry, Guid planId, int planVersion, long size)
    {
      this.metadata = metadata;
      this.displayJson = displayJson;
      this.metadataJson = metadataJson;
      this.planId = planId;
      this.planVersion = planVersion;
      Geometry = geometry;
      Size = size;
    }

    public RoutePlan ReadPlan(Guid? knownPlanId = null, int? knownVersion = null) =>
      JsonSerializer.Deserialize<RoutePlan>(knownPlanId == planId && knownVersion == planVersion
        ? metadataJson : displayJson, RoutePlanningService.Json)!;

    public RoutePlan ReadMetadata() => JsonSerializer.Deserialize<RoutePlan>(metadataJson, RoutePlanningService.Json)!;

    private static DispatchRoutePlan Copy(DispatchRoutePlan entity) => new()
    {
      Id = entity.Id, DispatchId = entity.DispatchId, TruckId = entity.TruckId,
      InputHash = entity.InputHash, CreatedAt = entity.CreatedAt
    };
  }

  public static Snapshot Create(DispatchRoutePlan entity)
  {
    var plan = JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, RoutePlanningService.Json)!;
    var geometry = new RouteGeometry(plan.Route);
    var points = plan.Route.Legs.Sum(x => (long)x.Points.Count);
    PlanningReadService.TrimForDisplay(plan);
    var displayJson = JsonSerializer.SerializeToUtf8Bytes(plan, RoutePlanningService.Json);
    PlanningReadService.TrimForDisplay(plan, plan.Id, plan.Version);
    var metadataJson = JsonSerializer.SerializeToUtf8Bytes(plan, RoutePlanningService.Json);
    var metadata = new DispatchRoutePlan { Id = entity.Id, DispatchId = entity.DispatchId, TruckId = entity.TruckId,
      InputHash = entity.InputHash, CreatedAt = entity.CreatedAt };
    return new(metadata, displayJson, metadataJson, geometry, plan.Id, plan.Version,
      displayJson.LongLength + metadataJson.LongLength + points * 48 + metadata.InputHash.Length * 2L + 2048);
  }

  public async Task<Snapshot?> GetAsync(Guid id, Func<Task<DispatchRoutePlan?>> load, CancellationToken ct)
  {
    var version = reads.Generation($"route:{id}");
    if (cache.TryGetValue<Cached>(id, out var hit) && hit!.Generation == version) return hit.Value;
    var gate = gates.For(id);
    await GateWait.WaitAsync(gate, "RouteDisplay", ct);
    try
    {
      if (cache.TryGetValue<Cached>(id, out hit) && hit!.Generation == version) return hit.Value;
      await coldLoads.WaitAsync(ct);
      try
      {
        var entity = await load();
        if (entity is null) return null;
        var result = Create(entity);
        if (version == reads.Generation($"route:{id}") && result.Size <= Capacity)
          cache.Set(id, new Cached(version, result), new MemoryCacheEntryOptions { Size = result.Size, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });
        return result;
      }
      finally { coldLoads.Release(); }
    }
    finally { gate.Release(); }
  }

  public void Dispose() { cache.Dispose(); gates.Dispose(); coldLoads.Dispose(); }
}
