namespace Domain.Entities.Dispatch;

public sealed class DispatchBaseRoute : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public string InputHash { get; set; } = "";
  public string RouteJson { get; set; } = "";
  public DateTime CalculatedAt { get; set; }

  // BorderVerdict.Stored: the country the road enters although every point
  // it was asked for is in one other country, or "stays", "unknown" or
  // "n/a"; null for a road saved before the check (BaseRoadBorderCheck).
  public string? BorderCheck { get; set; }
}
