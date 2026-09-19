namespace Client.Models.DTO.Planning;

public sealed record DriverCycleSnapshot(
  DateTime CalculatedAt,
  DateTime ValidUntil,
  StopCycleForecast Cycle
);
