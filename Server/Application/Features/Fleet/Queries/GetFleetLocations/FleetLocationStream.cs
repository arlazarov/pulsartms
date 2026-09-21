using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;

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

  public async Task<IReadOnlyList<TruckLocation>> GetAsync(
    IFleetTelemetryProvider provider,
    IReadOnlyList<FleetTruckInfo> fleet,
    CancellationToken ct = default
  )
  {
    await gate.WaitAsync(ct);
    try
    {
      var active = fleet
        .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.TruckExternalId))
        .ToDictionary(
          x => x.TruckExternalId,
          x => new FleetTruckInfo
          {
            TruckId = x.TruckId,
            TruckExternalId = x.TruckExternalId,
            UnitNumber = x.UnitNumber,
            DriverName = x.DriverName,
            TrailerNumber = x.TrailerNumber,
            IsActive = true,
          },
          StringComparer.OrdinalIgnoreCase
        );
      if (active.Count == 0)
      {
        retained = [];
        fleetIds.Clear();
        through = fullRefresh = null;
        return [];
      }

      var end = clock.GetUtcNow().UtcDateTime;
      var cutoff = end.AddSeconds(-60);
      var continuing =
        through is { } last
        && last <= end
        && last > cutoff
        && fleetIds.SetEquals(active.Keys);
      var reconcile = !continuing || fullRefresh <= end.AddSeconds(-30);
      var start = cutoff;
      if (!reconcile && through!.Value.AddSeconds(-10) > cutoff)
        start = through.Value.AddSeconds(-10);
      // Retained points are copied on ingress and never exposed or mutated.
      var points = (reconcile ? [] : retained)
        .Where(x => x.UpdatedAt >= cutoff)
        .ToDictionary(Key, LocationKeyComparer.Instance);
      var size = points.Values.Sum(Size);
      var externalIds = active.Keys.ToArray();
      var pages = 0;
      var cursors = new HashSet<string>(StringComparer.Ordinal);
      string? cursor = null;
      while (true)
      {
        if (++pages > 256)
          throw new InvalidOperationException(
            "Telemetry stream exceeded the bounded page limit."
          );
        var page = await provider.GetLocationStreamAsync(
          externalIds,
          start,
          end,
          cursor,
          ct
        );
        foreach (
          var point in page.Data.Where(x =>
            active.ContainsKey(x.ExternalId)
            && x.UpdatedAt >= start
            && x.UpdatedAt <= end
          )
        )
        {
          var key = Key(point);
          if (points.TryGetValue(key, out var previous))
            size -= Size(previous);
          size += Size(point);
          if (
            size > MaximumBytes
            || !points.ContainsKey(key) && points.Count >= MaximumPoints
          )
            throw new InvalidOperationException(
              "Telemetry stream exceeded the bounded point limit."
            );
          points[key] = Copy(point);
        }
        if (!page.HasNextPage)
          break;
        if (
          string.IsNullOrWhiteSpace(page.EndCursor)
          || !cursors.Add(page.EndCursor)
        )
          throw new InvalidOperationException(
            "Telemetry returned an invalid pagination cursor."
          );
        cursor = page.EndCursor;
      }
      ct.ThrowIfCancellationRequested();
      var ordered = points.Values.OrderBy(x => x.UpdatedAt).ToArray();
      retained = ordered;
      through = end;
      if (reconcile)
        fullRefresh = end;
      fleetIds = active.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

      return ordered
        .Select(point =>
        {
          var truck = active[point.ExternalId];
          return new TruckLocation
          {
            TruckId = truck.TruckId,
            TruckExternalId = truck.TruckExternalId,
            UnitNumber = truck.UnitNumber,
            DriverName = truck.DriverName,
            TrailerNumber = truck.TrailerNumber,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            Speed = point.Speed,
            Heading = point.Heading,
            UpdatedAt = point.UpdatedAt,
            ObservedAt = end,
            FormattedLocation = point.FormattedLocation,
          };
        })
        .ToArray();
    }
    finally
    {
      gate.Release();
    }
  }

  private static (string Id, DateTime Time) Key(VehicleLocationPoint point) =>
    (point.ExternalId, point.UpdatedAt);

  private sealed class LocationKeyComparer
    : IEqualityComparer<(string Id, DateTime Time)>
  {
    public static LocationKeyComparer Instance { get; } = new();

    public bool Equals(
      (string Id, DateTime Time) x,
      (string Id, DateTime Time) y
    ) =>
      x.Time == y.Time && StringComparer.OrdinalIgnoreCase.Equals(x.Id, y.Id);

    public int GetHashCode((string Id, DateTime Time) key) =>
      HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(key.Id),
        key.Time
      );
  }

  private static long Size(VehicleLocationPoint point) =>
    128L + 2L * (point.ExternalId.Length + point.FormattedLocation.Length);

  private static VehicleLocationPoint Copy(VehicleLocationPoint point) =>
    new()
    {
      ExternalId = point.ExternalId,
      Latitude = point.Latitude,
      Longitude = point.Longitude,
      Speed = point.Speed,
      Heading = point.Heading,
      UpdatedAt = point.UpdatedAt,
      FormattedLocation = point.FormattedLocation,
    };

  public void Dispose() => gate.Dispose();
}
