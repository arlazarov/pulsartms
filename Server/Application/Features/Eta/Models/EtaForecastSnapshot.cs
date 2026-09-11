namespace Application.Features.Eta.Models;

public sealed record EtaForecastSnapshot(Guid DispatchId, Guid TruckId, Guid RootDispatchId,
  string InputHash, string DriverExternalId, DispatchEta Forecast);
