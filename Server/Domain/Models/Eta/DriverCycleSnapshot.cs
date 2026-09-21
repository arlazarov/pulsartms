namespace Domain.Models.Eta;

public sealed record DriverCycleSnapshot(
  DateTime CalculatedAt,
  DateTime ValidUntil,
  StopCycleForecast Cycle
);
