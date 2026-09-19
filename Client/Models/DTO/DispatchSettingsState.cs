namespace Client.Models.DTO;

public sealed record DispatchSettingsState(
  string LoadNumberPrefix,
  long Revision,
  DateTime? UpdatedAt,
  string TemperatureUnit = "both",
  string DistanceUnit = "both"
);
