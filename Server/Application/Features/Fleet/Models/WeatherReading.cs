namespace Application.Features.Fleet.Models;

public sealed record WeatherReading(
  decimal Celsius,
  string Condition,
  string Description,
  bool IsDaytime,
  DateTimeOffset UpdatedAt
);
