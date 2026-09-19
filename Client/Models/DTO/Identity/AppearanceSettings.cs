namespace Client.Models.DTO.Identity;

public sealed record AppearanceSettings(
  string Theme,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
);
