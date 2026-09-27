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

  // Hashes of the work and road the root's forecast was calculated for
  // (EtaService), so another process decides whether it is current for a
  // plan as the owner's memory does. Null on followers' rows and on rows
  // saved before stage 4e: such a row is never shown for a plan.
  public string? WorkKey { get; set; }
  public string? RouteKey { get; set; }
}
