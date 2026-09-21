using Domain.Models.Fleet;

namespace Application.Features.Synchronization.Models;

public sealed class SynchronizationState
{
  public Dictionary<Guid, SynchronizationState> Companies { get; set; } = new();

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
        Vehicles[update.ExternalId] = vehicle = new()
        {
          ExternalId = update.ExternalId,
        };
      if (update.Gps is { } gps)
        ApplyLocation(vehicle, gps);
      if (
        update.EngineUpdatedAt is { } engineAt
        && (
          vehicle.EngineUpdatedAt is null || engineAt > vehicle.EngineUpdatedAt
        )
      )
      {
        vehicle.EngineState = update.EngineState ?? "";
        vehicle.EngineUpdatedAt = engineAt;
      }
      if (
        update.FuelUpdatedAt is { } fuelAt
        && (vehicle.FuelUpdatedAt is null || fuelAt > vehicle.FuelUpdatedAt)
      )
      {
        vehicle.FuelPercent = update.FuelPercent;
        vehicle.FuelUpdatedAt = fuelAt;
      }
      if (
        update.OutsideTemperatureCelsius is not null
        && update.OutsideTemperatureUpdatedAt is { } outsideAt
        && (
          vehicle.OutsideTemperatureUpdatedAt is null
          || outsideAt > vehicle.OutsideTemperatureUpdatedAt
        )
      )
      {
        vehicle.OutsideTemperatureCelsius = update.OutsideTemperatureCelsius;
        vehicle.OutsideTemperatureUpdatedAt = outsideAt;
      }
    }
    TelemetryCursor = cursor;
  }

  public void ApplyLocations(IEnumerable<VehicleLocationPoint> points)
  {
    foreach (var point in points)
    {
      if (!Vehicles.TryGetValue(point.ExternalId, out var vehicle))
        Vehicles[point.ExternalId] = vehicle = new()
        {
          ExternalId = point.ExternalId,
        };
      ApplyLocation(vehicle, point);
    }
  }

  private static void ApplyLocation(
    VehicleTelemetry vehicle,
    VehicleLocationPoint point
  )
  {
    if (point.UpdatedAt <= vehicle.UpdatedAt)
      return;
    vehicle.Latitude = point.Latitude;
    vehicle.Longitude = point.Longitude;
    vehicle.Speed = point.Speed;
    vehicle.Heading = point.Heading;
    vehicle.UpdatedAt = point.UpdatedAt;
    vehicle.FormattedLocation = point.FormattedLocation;
  }
}
