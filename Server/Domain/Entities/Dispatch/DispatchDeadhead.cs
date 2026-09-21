namespace Domain.Entities.Dispatch;

public sealed class DispatchDeadhead : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public Guid PreviousDispatchId { get; set; }
  public Guid? PreviousExecutionLegId { get; set; }
  public string InputHash { get; set; } = "";
  public decimal? Miles { get; set; }
  public DateTime? CalculatedAt { get; set; }
  public DateTime RetryAfter { get; set; }
  public string? ErrorMessage { get; set; }
  public string? RouteJson { get; set; }
}
