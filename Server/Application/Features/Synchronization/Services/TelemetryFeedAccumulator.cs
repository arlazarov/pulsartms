using Application.Features.Fleet.Models;

namespace Application.Features.Synchronization.Services;

public sealed class TelemetryFeedAccumulator(DateTime observedAt)
{
  private const long MaximumBytes = 16 * 1024 * 1024;
  private readonly Dictionary<string, TelemetryUpdate> vehicles = new(
    StringComparer.Ordinal
  );
  private readonly Dictionary<(string, DateTime), VehicleLocationPoint> points =
    new();
  private long bytes;
  private int pages;
  public IReadOnlyCollection<TelemetryUpdate> Updates => vehicles.Values;
  public IReadOnlyCollection<VehicleLocationPoint> Points => points.Values;

  public void Append(TelemetryFeed feed)
  {
    if (++pages > 256)
      throw TooLarge();
    foreach (var update in feed.Updates)
    {
      var previous = vehicles.GetValueOrDefault(update.ExternalId);
      var gps =
        previous?.Gps is null || update.Gps?.UpdatedAt > previous.Gps.UpdatedAt
          ? update.Gps
          : previous.Gps;
      var engine =
        previous?.EngineUpdatedAt is null
        || update.EngineUpdatedAt > previous.EngineUpdatedAt
          ? update
          : previous;
      var fuel =
        previous?.FuelUpdatedAt is null
        || update.FuelUpdatedAt > previous.FuelUpdatedAt
          ? update
          : previous;
      var outside =
        previous?.OutsideTemperatureUpdatedAt is null
        || (
          update.OutsideTemperatureCelsius is not null
          && update.OutsideTemperatureUpdatedAt
            > previous.OutsideTemperatureUpdatedAt
        )
          ? update
          : previous;
      var merged = new TelemetryUpdate(
        update.ExternalId,
        gps,
        engine.EngineState,
        engine.EngineUpdatedAt,
        fuel.FuelPercent,
        fuel.FuelUpdatedAt,
        outside.OutsideTemperatureCelsius,
        outside.OutsideTemperatureUpdatedAt
      );
      bytes += Size(merged) - (previous is null ? 0 : Size(previous));
      vehicles[update.ExternalId] = merged;
      if (
        update.Gps is { } point
        && point.UpdatedAt > observedAt.AddMinutes(-2)
      )
      {
        var key = (update.ExternalId, point.UpdatedAt);
        if (points.TryGetValue(key, out var old))
          bytes -= PointSize(old);
        points[key] = point;
        bytes += PointSize(point);
      }
      if (
        vehicles.Count > 10000
        || points.Count > 65536
        || bytes > MaximumBytes
      )
        throw TooLarge();
    }
  }

  private static long PointSize(VehicleLocationPoint point) =>
    128L + 2L * (point.ExternalId.Length + point.FormattedLocation.Length);

  private static long Size(TelemetryUpdate value) =>
    160L
    + value.ExternalId.Length * 2L
    + (value.EngineState?.Length ?? 0) * 2L
    + (value.Gps is null ? 0 : PointSize(value.Gps));

  private static InvalidOperationException TooLarge() =>
    new("Telemetry feed exceeded the bounded synchronization limit.");
}
