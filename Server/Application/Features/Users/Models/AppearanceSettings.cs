namespace Application.Features.Users.Models;

// Theme is null when the user has not chosen one; the Client applies the
// product default then.
public sealed record AppearanceSettings(
  string? Theme,
  string? TemperatureUnit = null,
  string? DistanceUnit = null
);
