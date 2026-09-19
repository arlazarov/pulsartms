using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public sealed class FleetLocationStream(TimeProvider clock) : IDisposable
{
  private const int MaximumPoints = 65536;
  private const long MaximumBytes = 16 * 1024 * 1024;
  private readonly SemaphoreSlim gate = new(1, 1);
  private IReadOnlyList<VehicleLocationPoint> retained = [];
  private HashSet<string> fleetIds = new(StringComparer.OrdinalIgnoreCase);
  private DateTime? through;
  private DateTime? fullRefresh;

  public async Task<IReadOnlyList<TruckLocationPoint>> GetAsync(IFleetTelemetryProvider provider,
    IReadOnlyList<FleetTruckInfo> fleet, CancellationToken ct = default)
  {
    await gate.WaitAsync(ct);
    try
    {
      var active = fleet.Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.TruckExternalId))
        .ToDictionary(x => x.TruckExternalId, x => new FleetTruckInfo
        {
          TruckId = x.TruckId, TruckExternalId = x.TruckExternalId, UnitNumber = x.UnitNumber,
          DriverName = x.DriverName, TrailerNumber = x.TrailerNumber, IsActive = true
        }, StringComparer.OrdinalIgnoreCase);
      if (active.Count == 0)
      {
        retained = []; fleetIds.Clear(); through = fullRefresh = null;
        return [];
      }

      var end = clock.GetUtcNow().UtcDateTime;
      var cutoff = end.AddSeconds(-60);
      var continuing = through is { } last && last <= end && last > cutoff && fleetIds.SetEquals(active.Keys);
      var reconcile = !continuing || fullRefresh <= end.AddSeconds(-30);
      var start = reconcile ? cutoff : new[] { cutoff, through!.Value.AddSeconds(-10) }.Max();
      var points = (reconcile ? [] : retained).Where(x => x.UpdatedAt >= cutoff).ToDictionary(Key, Copy);
      var cursors = new HashSet<string>(StringComparer.Ordinal);
      string? cursor = null;
      while (true)
      {
        var page = await provider.GetLocationStreamAsync([.. active.Keys], start, end, cursor, ct);
        foreach (var point in page.Data.Where(x => active.ContainsKey(x.ExternalId) && x.UpdatedAt >= start && x.UpdatedAt <= end))
          points[Key(point)] = Copy(point);
        if (!page.HasNextPage) break;
        if (string.IsNullOrWhiteSpace(page.EndCursor) || !cursors.Add(page.EndCursor))
          throw new InvalidOperationException("Telemetry returned an invalid pagination cursor.");
        cursor = page.EndCursor;
      }
      ct.ThrowIfCancellationRequested();
      var ordered = points.Values.OrderBy(x => x.UpdatedAt).ToArray();
      var size = ordered.Sum(x => 128L + 2L * (x.ExternalId.Length + x.FormattedLocation.Length));
      if (ordered.Length <= MaximumPoints && size <= MaximumBytes)
      {
        retained = ordered;
        through = end;
        if (reconcile) fullRefresh = end;
        fleetIds = active.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
      }
      else
      {
        // Preserve this complete response; an oversized window is not retained or acknowledged.
        retained = []; fleetIds.Clear(); through = fullRefresh = null;
      }

      return ordered.Select(point => new TruckLocationPoint(active[point.ExternalId].TruckExternalId,
        point.Latitude, point.Longitude, point.Speed, point.Heading, point.UpdatedAt)).ToArray();
    }
    finally { gate.Release(); }
  }

  private static (string Id, DateTime Time) Key(VehicleLocationPoint point) => (point.ExternalId.ToUpperInvariant(), point.UpdatedAt);
  private static VehicleLocationPoint Copy(VehicleLocationPoint point) => new()
  {
    ExternalId = point.ExternalId, Latitude = point.Latitude, Longitude = point.Longitude, Speed = point.Speed,
    Heading = point.Heading, UpdatedAt = point.UpdatedAt, FormattedLocation = point.FormattedLocation
  };

  public void Dispose() => gate.Dispose();
}
