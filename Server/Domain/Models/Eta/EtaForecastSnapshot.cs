namespace Domain.Models.Eta;

public sealed record EtaForecastSnapshot(
  Guid DispatchId,
  Guid TruckId,
  Guid RootDispatchId,
  string InputHash,
  string DriverExternalId,
  DispatchEta Forecast
)
{
  public Guid? ExecutionLegId { get; init; }
  public Guid? RootExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }

  // DispatchEtaForecast.WorkKey and RouteKey.
  public string? WorkKey { get; init; }
  public string? RouteKey { get; init; }
}
