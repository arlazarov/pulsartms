using System.Text.Json;
using Application.Caching;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Routing.Services.Routes;

public sealed class RouteDisplayCache(ReadCache reads)
  : IDisposable,
    ICacheMemorySource
{
  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    var stats0 = cache.GetCurrentStatistics();
    var stats1 = exactIndexes.GetCurrentStatistics();
    return
    [
      new(
        "route-display",
        stats0?.CurrentEntryCount,
        stats0?.CurrentEstimatedSize,
        CacheBudgets.RouteDisplay,
        "bytes"
      ),
      new(
        "route-indexes",
        stats1?.CurrentEntryCount,
        stats1?.CurrentEstimatedSize,
        CacheBudgets.RouteIndexes,
        "bytes"
      ),
    ];
  }

  private const long Capacity = CacheBudgets.RouteDisplay;
  private readonly MemoryCache exactIndexes = new(
    new MemoryCacheOptions
    {
      TrackStatistics = true,
      SizeLimit = CacheBudgets.RouteIndexes,
    }
  );

  public RouteGeometry ExactGeometry(DispatchRoutePlan entity, RoutePlan plan)
  {
    var key = (
      entity.CompanyId,
      entity.Id,
      entity.GeometryRevision,
      entity.GeometryManifestJson ?? entity.PlanJson
    );
    if (exactIndexes.TryGetValue<RouteGeometry>(key, out var existing))
      return existing!;
    var geometry = new RouteGeometry(plan.Route);
    var bytes = geometry.EstimatedBytes + key.Item4.Length * 2L + 256;
    if (bytes <= CacheBudgets.RouteIndexes)
      exactIndexes.Set(
        key,
        geometry,
        new MemoryCacheEntryOptions
        {
          Size = bytes,
          SlidingExpiration = TimeSpan.FromMinutes(5),
        }
      );
    return geometry;
  }

  private readonly MemoryCache cache = new(
    new MemoryCacheOptions { TrackStatistics = true, SizeLimit = Capacity }
  );
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

    internal Snapshot(
      DispatchRoutePlan metadata,
      byte[] displayJson,
      byte[] metadataJson,
      RouteGeometry geometry,
      Guid planId,
      int planVersion,
      long size
    )
    {
      this.metadata = metadata;
      this.displayJson = displayJson;
      this.metadataJson = metadataJson;
      this.planId = planId;
      this.planVersion = planVersion;
      Geometry = geometry;
      Size = size;
    }

    public RoutePlan ReadPlan(
      Guid? knownPlanId = null,
      int? knownVersion = null
    ) =>
      JsonSerializer.Deserialize<RoutePlan>(
        knownPlanId == planId && knownVersion == planVersion
          ? metadataJson
          : displayJson,
        RoutingJson.Options
      )!;

    public RoutePlan ReadMetadata() =>
      JsonSerializer.Deserialize<RoutePlan>(metadataJson, RoutingJson.Options)!;

    private static DispatchRoutePlan Copy(DispatchRoutePlan entity) =>
      new()
      {
        Id = entity.Id,
        CompanyId = entity.CompanyId,
        AssignmentRevision = entity.AssignmentRevision,
        GeometryRevision = entity.GeometryRevision,
        DispatchId = entity.DispatchId,
        ExecutionLegId = entity.ExecutionLegId,
        TruckId = entity.TruckId,
        InputHash = entity.InputHash,
        CreatedAt = entity.CreatedAt,
      };
  }

  public static Snapshot Create(DispatchRoutePlan entity) =>
    Create(entity, RoutePlanStorage.Read(entity)!);

  private static Snapshot Create(
    DispatchRoutePlan entity,
    RoutePlan plan,
    RouteGeometry? exact = null
  )
  {
    var geometry = exact ?? new RouteGeometry(plan.Route);
    PlanningReadService.TrimForDisplay(plan);
    var displayJson = JsonSerializer.SerializeToUtf8Bytes(
      plan,
      RoutingJson.Options
    );
    PlanningReadService.TrimForDisplay(plan, plan.Id, plan.Version);
    var metadataJson = JsonSerializer.SerializeToUtf8Bytes(
      plan,
      RoutingJson.Options
    );
    var metadata = new DispatchRoutePlan
    {
      Id = entity.Id,
      CompanyId = entity.CompanyId,
      AssignmentRevision = entity.AssignmentRevision,
      GeometryRevision = entity.GeometryRevision,
      DispatchId = entity.DispatchId,
      ExecutionLegId = entity.ExecutionLegId,
      TruckId = entity.TruckId,
      InputHash = entity.InputHash,
      CreatedAt = entity.CreatedAt,
    };
    return new(
      metadata,
      displayJson,
      metadataJson,
      geometry,
      plan.Id,
      plan.Version,
      displayJson.LongLength
        + metadataJson.LongLength
        + geometry.EstimatedBytes
        + metadata.InputHash.Length * 2L
        + 2048
    );
  }

  public async Task<Snapshot?> GetAsync(
    Guid id,
    Func<Task<DispatchRoutePlan?>> load,
    CancellationToken ct,
    Guid? executionLegId = null
  )
  {
    var key = RoutePlanStore.CacheKey(id, executionLegId);
    var version = reads.Generation(key);
    if (
      cache.TryGetValue<Cached>(key, out var hit)
      && hit!.Generation == version
    )
      return hit.Value;
    var gate = gates.For(executionLegId ?? id);
    await GateWait.WaitAsync(gate, "RouteDisplay", ct);
    try
    {
      if (cache.TryGetValue<Cached>(key, out hit) && hit!.Generation == version)
        return hit.Value;
      await coldLoads.WaitAsync(ct);
      try
      {
        var entity = await load();
        if (entity is null)
          return null;
        if (
          entity.ExecutionLegId != executionLegId
          || (executionLegId.HasValue && entity.DispatchId != id)
        )
          return null;
        var plan = RoutePlanStorage.Read(entity)!;
        var result = Create(entity, plan, ExactGeometry(entity, plan));
        if (version == reads.Generation(key) && result.Size <= Capacity)
          cache.Set(
            key,
            new Cached(version, result),
            new MemoryCacheEntryOptions
            {
              Size = result.Size,
              AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
            }
          );
        return result;
      }
      finally
      {
        coldLoads.Release();
      }
    }
    finally
    {
      gate.Release();
    }
  }

  public void Dispose()
  {
    cache.Dispose();
    exactIndexes.Dispose();
    gates.Dispose();
    coldLoads.Dispose();
  }
}
