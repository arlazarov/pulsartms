namespace Domain.Models.Fleet;

public sealed record WeatherReading(
  decimal Celsius,
  string Condition,
  string Description,
  bool IsDaytime,
  DateTimeOffset UpdatedAt
);
