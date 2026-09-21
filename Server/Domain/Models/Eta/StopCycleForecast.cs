namespace Domain.Models.Eta;

public sealed record StopCycleForecast(
  int RemainingMinutes,
  DateTimeOffset? NextRecapAt,
  int? NextRecapMinutes,
  string? HomeTimeZoneId,
  bool RecapVerified
);
