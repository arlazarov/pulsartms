using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public record GetTruckHistoryQuery(Guid TruckId, DateTimeOffset From, DateTimeOffset To, bool Refresh = false)
  : IRequest<RequestResponse<IReadOnlyList<VehicleLocationPoint>>>;

public class GetTruckHistoryHandler(IAppDbContext db, IFleetTelemetryProvider telemetry, IMemoryCache cache, TruckHistoryQueue queue, Application.Caching.ProcessGates gates)
  : IRequestHandler<GetTruckHistoryQuery, RequestResponse<IReadOnlyList<VehicleLocationPoint>>>
{
  private sealed record Snapshot(IReadOnlyList<VehicleLocationPoint> Points, DateTime Through, DateTime FetchedAt)
  {
    private readonly Lazy<IReadOnlyList<VehicleLocationPoint>> simplified = new(() => TruckHistoryGeometry.Simplify(Points));
    public IReadOnlyList<VehicleLocationPoint> Simplified => simplified.Value;
  }
  public async Task<RequestResponse<IReadOnlyList<VehicleLocationPoint>>> Handle(GetTruckHistoryQuery request, CancellationToken ct)
  {
    var from = request.From.UtcDateTime;
    var to = request.To.UtcDateTime < DateTime.UtcNow ? request.To.UtcDateTime : DateTime.UtcNow;
    if (to <= from || request.To - request.From > TimeSpan.FromHours(25))
      return RequestResponse<IReadOnlyList<VehicleLocationPoint>>.Ok([]);
    var key = $"truck-history:{request.TruckId}:{from:O}:{request.To:O}";
    if (!request.Refresh)
    {
      cache.TryGetValue(key, out Snapshot? current);
      if (current is null || DateTime.UtcNow - current.FetchedAt >= TimeSpan.FromMinutes(1)) queue.Enqueue(request);
      return RequestResponse<IReadOnlyList<VehicleLocationPoint>>.Ok(current?.Simplified ?? []);
    }
    var gate = gates.For<GetTruckHistoryHandler>().For(request.TruckId);
    await gate.WaitAsync(ct);
    try
    {
      cache.TryGetValue(key, out Snapshot? saved);
      if (saved is null || DateTime.UtcNow - saved.FetchedAt >= TimeSpan.FromMinutes(1))
      {
        var externalId = await db.Trucks.AsNoTracking().Where(x => x.Id == request.TruckId).Select(x => x.ExternalId).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(externalId)) return RequestResponse<IReadOnlyList<VehicleLocationPoint>>.Ok([]);
        var fetchFrom = saved is null ? from : new[] { from, saved.Through.AddMinutes(-2) }.Max();
        var points = saved?.Points.Where(p => p.UpdatedAt < fetchFrom).ToList() ?? [];
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
          var page = await telemetry.GetLocationStreamAsync([externalId], fetchFrom, to, cursor, ct);
          points.AddRange(page.Data.Where(p => p.ExternalId == externalId && p.UpdatedAt >= from && p.UpdatedAt <= to));
          cache.Set(key, new Snapshot(points.OrderBy(p => p.UpdatedAt).DistinctBy(p => p.UpdatedAt).ToArray(), saved?.Through ?? from, saved?.FetchedAt ?? DateTime.MinValue), TimeSpan.FromHours(26));
          if (!page.HasNextPage) break;
          if (string.IsNullOrEmpty(page.EndCursor) || !cursors.Add(page.EndCursor)) throw new InvalidOperationException("Invalid history cursor.");
          cursor = page.EndCursor;
        } while (true);
        saved = new Snapshot(points.OrderBy(p => p.UpdatedAt).DistinctBy(p => p.UpdatedAt).ToArray(), to, DateTime.UtcNow);
        cache.Set(key, saved, TimeSpan.FromHours(26));
      }
      return RequestResponse<IReadOnlyList<VehicleLocationPoint>>.Ok(saved.Simplified);
    }
    finally { gate.Release(); }
  }
}
