namespace Application.Features.Eta.Models;

public sealed record DriverCycleSnapshot(
  DateTime CalculatedAt,
  DateTime ValidUntil,
  StopCycleForecast Cycle
);
