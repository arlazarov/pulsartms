namespace Domain.Entities.Dispatch;

public sealed class DispatchEtaForecast : BaseEntity
{
  public Guid DispatchId { get; set; }
  public Guid TruckId { get; set; }
  public Guid RootDispatchId { get; set; }
  public string InputHash { get; set; } = "";
  public string DriverExternalId { get; set; } = "";
  public DateTime CalculatedAt { get; set; }
  public DateTime ValidUntil { get; set; }
  public string ForecastJson { get; set; } = "";
}
