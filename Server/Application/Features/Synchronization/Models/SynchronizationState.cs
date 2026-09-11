using Application.Features.Fleet.Models;

namespace Application.Features.Synchronization.Models;

public sealed class SynchronizationState
{
  public string? TelemetryCursor { get; set; }
  public Dictionary<string, VehicleTelemetry> Vehicles { get; set; } = new();
  public Dictionary<string, SyncJobState> Jobs { get; set; } = new();
  public List<Guid> PendingTrucks { get; set; } = [];
  public DateTime LastPlanningScan { get; set; }

  public void Apply(IEnumerable<TelemetryUpdate> updates, string cursor)
  {
    foreach (var update in updates)
    {
      if (!Vehicles.TryGetValue(update.ExternalId, out var vehicle))
        Vehicles[update.ExternalId] = vehicle = new() { ExternalId = update.ExternalId };
      if (update.Gps is { } gps && gps.UpdatedAt > vehicle.UpdatedAt)
      {
        vehicle.Latitude = gps.Latitude; vehicle.Longitude = gps.Longitude;
        vehicle.Speed = gps.Speed; vehicle.Heading = gps.Heading; vehicle.UpdatedAt = gps.UpdatedAt;
        vehicle.FormattedLocation = gps.FormattedLocation;
      }
      if (update.EngineUpdatedAt is { } engineAt && (vehicle.EngineUpdatedAt is null || engineAt > vehicle.EngineUpdatedAt))
      { vehicle.EngineState = update.EngineState ?? ""; vehicle.EngineUpdatedAt = engineAt; }
      if (update.FuelUpdatedAt is { } fuelAt && (vehicle.FuelUpdatedAt is null || fuelAt > vehicle.FuelUpdatedAt))
      { vehicle.FuelPercent = update.FuelPercent; vehicle.FuelUpdatedAt = fuelAt; }
    }
    TelemetryCursor = cursor;
  }
}
