namespace Application.Features.Fleet.Models;

public sealed record TelemetryUpdate(
  string ExternalId,
  VehicleLocationPoint? Gps,
  string? EngineState,
  DateTime? EngineUpdatedAt,
  decimal? FuelPercent,
  DateTime? FuelUpdatedAt,
  decimal? OutsideTemperatureCelsius = null,
  DateTime? OutsideTemperatureUpdatedAt = null
);

public sealed record TelemetryFeed(
  IReadOnlyList<TelemetryUpdate> Updates,
  string Cursor,
  bool HasNextPage
);
