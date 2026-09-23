namespace Application.Features.Dispatch.Models;

public sealed record DispatchSettingsState(
  string LoadNumberPrefix,
  long Revision,
  DateTime? UpdatedAt,
  string TemperatureUnit = "both",
  string DistanceUnit = "both",
  bool AutomaticFuelSending = false
);
