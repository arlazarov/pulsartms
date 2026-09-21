using Domain.Entities;

namespace Domain.Entities.Dispatch;

public sealed class DispatchEtaForecast : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public Guid TruckId { get; set; }
  public Guid RootDispatchId { get; set; }
  public Guid? RootExecutionLegId { get; set; }
  public string InputHash { get; set; } = "";
  public string DriverExternalId { get; set; } = "";
  public DateTime CalculatedAt { get; set; }
  public DateTime ValidUntil { get; set; }
  public string ForecastJson { get; set; } = "";
}
