using Application.Caching;
using Application.Interfaces;
using Application.Models;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public sealed class TruckHistoryCache(ICurrentCompany companies)
  : IDisposable,
    ICacheMemorySource
{
  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    var stats0 = cache.GetCurrentStatistics();
    return
    [
      new(
        "truck-history",
        stats0?.CurrentEntryCount,
        stats0?.CurrentEstimatedSize,
        Capacity,
        "bytes"
      ),
    ];
  }

  public const long Capacity = CacheBudgets.TruckHistory;
  private readonly MemoryCache cache = new(
    new MemoryCacheOptions { TrackStatistics = true, SizeLimit = Capacity }
  );

  public sealed record Snapshot(
    IReadOnlyList<VehicleLocationPoint> Points,
    DateTime Through,
    DateTime FetchedAt
  )
  {
    private readonly Lazy<IReadOnlyList<VehicleLocationPoint>> simplified = new(
      () => TruckHistoryGeometry.Simplify(Points)
    );
    public IReadOnlyList<VehicleLocationPoint> Simplified => simplified.Value;
  }

  public Snapshot? Get(string key) =>
    companies.Id is { } company ? cache.Get<Snapshot>((company, key)) : null;

  public void Set(string key, Snapshot snapshot)
  {
    if (companies.Id is not { } company)
      return;
    // Include both point-reference arrays and conservatively count strings
    // per point, even when a provider shares their instances.
    var size = 1024L + key.Length * 2L;
    foreach (var point in snapshot.Points)
      size +=
        192L
        + point.ExternalId.Length * 2L
        + point.FormattedLocation.Length * 2L;
    if (size > Capacity)
      return;
    cache.Set(
      (company, key),
      snapshot,
      new MemoryCacheEntryOptions
      {
        Size = size,
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(26),
      }
    );
  }

  public void Dispose() => cache.Dispose();
}
