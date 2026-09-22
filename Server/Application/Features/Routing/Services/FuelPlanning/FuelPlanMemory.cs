using Application.Caching;
using Application.Interfaces;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelPlanMemory(ICurrentCompany companies)
  : IDisposable,
    ICacheMemorySource
{
  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    var stats0 = cache.GetCurrentStatistics();
    return
    [
      new(
        "fuel",
        stats0?.CurrentEntryCount,
        stats0?.CurrentEstimatedSize,
        CacheBudgets.Fuel,
        "bytes"
      ),
    ];
  }

  private readonly MemoryCache cache = new(
    new MemoryCacheOptions
    {
      TrackStatistics = true,
      SizeLimit = CacheBudgets.Fuel,
    }
  );
  private readonly KeyedGates gates = new();
  private readonly SemaphoreSlim priceGate = new(1);
  private readonly SemaphoreSlim coldLoads = new(2, 2);

  private sealed record CachedLeg(
    DateTime CalculatedAt,
    Guid RootDispatchId,
    Guid? ExecutionLegId,
    long AssignmentRevision,
    int Index,
    RouteGeometry Geometry
  );

  public async Task<string?> PricesAsync(
    string key,
    Func<Task<string?>> load,
    CancellationToken ct
  )
  {
    if (companies.Id is not { } company)
      return null;
    key = $"{company:N}:{key}";
    if (cache.TryGetValue<string>(key, out var found))
      return found;
    await priceGate.WaitAsync(ct);
    try
    {
      if (cache.TryGetValue<string>(key, out found))
        return found;
      found = await load();
      if (found is not null)
        cache.Set(
          key,
          found,
          new MemoryCacheEntryOptions
          {
            Size = 512,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30),
          }
        );
      return found;
    }
    finally
    {
      priceGate.Release();
    }
  }

  public async Task<RouteGeometry?> LegAsync(
    TruckFuelPlanSnapshot snapshot,
    int index,
    Func<Task<TruckFuelPlanSnapshot?>> load,
    CancellationToken ct
  )
  {
    if (companies.Id is not { } company)
      return null;
    var key = (company, snapshot.TruckId);
    if (
      cache.TryGetValue<CachedLeg>(key, out var found)
      && found!.CalculatedAt == snapshot.CalculatedAt
      && found.RootDispatchId == snapshot.RootDispatchId
      && found.ExecutionLegId == snapshot.RootExecutionLegId
      && found.AssignmentRevision == snapshot.AssignmentRevision
      && found.Index == index
    )
      return found.Geometry;
    var gate = gates.For(snapshot.TruckId);
    await gate.WaitAsync(ct);
    try
    {
      if (
        cache.TryGetValue<CachedLeg>(key, out found)
        && found!.CalculatedAt == snapshot.CalculatedAt
        && found.RootDispatchId == snapshot.RootDispatchId
        && found.ExecutionLegId == snapshot.RootExecutionLegId
        && found.AssignmentRevision == snapshot.AssignmentRevision
        && found.Index == index
      )
        return found.Geometry;
      await coldLoads.WaitAsync(ct);
      try
      {
        var full = await load();
        if (
          full?.CalculatedAt != snapshot.CalculatedAt
          || full.TruckId != snapshot.TruckId
          || full.RootDispatchId != snapshot.RootDispatchId
          || full.RootExecutionLegId != snapshot.RootExecutionLegId
          || full.AssignmentRevision != snapshot.AssignmentRevision
          || (
            full.Plan.EstimatedStationAccess
              ? full.BaselineRoute
              : full.CheckedRoute
          )
            is not { } route
          || index < 0
          || index >= route.Legs.Count
        )
          return null;
        var leg = route.Legs[index];
        var selected = new TruckRoute { Legs = [leg] };
        var size = Math.Max(1024L, RouteGeometry.EstimateBytes(selected));
        if (size > CacheBudgets.Fuel)
          return null;
        var geometry = new RouteGeometry(selected);
        if (found is null || found.CalculatedAt <= snapshot.CalculatedAt)
          cache.Set(
            key,
            new CachedLeg(
              snapshot.CalculatedAt,
              snapshot.RootDispatchId,
              snapshot.RootExecutionLegId,
              snapshot.AssignmentRevision,
              index,
              geometry
            ),
            new MemoryCacheEntryOptions
            {
              Size = size,
              AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            }
          );
        return geometry;
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
    gates.Dispose();
    priceGate.Dispose();
    coldLoads.Dispose();
  }
}
