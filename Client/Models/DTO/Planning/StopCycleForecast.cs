namespace Client.Models.DTO.Planning;

public sealed record StopCycleForecast(int RemainingMinutes, DateTimeOffset? NextRecapAt,
  int? NextRecapMinutes, string? HomeTimeZoneId, bool RecapVerified);
