namespace Domain.Entities.Execution;

public sealed class ExecutionSourceReceipt : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid IdempotencyKey { get; set; }
  public Guid ExecutionLegId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}
