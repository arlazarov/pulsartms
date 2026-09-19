namespace Client.Models.DTO;

public sealed record DispatchSettingsUpdate(
  string LoadNumberPrefix,
  long Revision,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
);
