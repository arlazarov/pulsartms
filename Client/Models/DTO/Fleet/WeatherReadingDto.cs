namespace Client.Models.DTO.Fleet;

public sealed record WeatherReadingDto(
  decimal Celsius,
  string Condition,
  string Description,
  bool IsDaytime,
  DateTimeOffset UpdatedAt
);
