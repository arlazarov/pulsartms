namespace Domain.Entities.Execution;

public sealed class ExecutionActionReceipt : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid IdempotencyKey { get; set; }
  public Guid SwitchId { get; set; }
  public Guid ParticipantId { get; set; }
  public string Action { get; set; } = "";
  public string RequestHash { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}
