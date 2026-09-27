namespace Application.Features.Users.Models;

public sealed record AppearanceSettings(
  string Theme,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
);
