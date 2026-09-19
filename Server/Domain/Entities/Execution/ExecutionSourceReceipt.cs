namespace Domain.Entities.Execution;

public sealed class ExecutionSourceReceipt : BaseEntity
{
  public Guid IdempotencyKey { get; set; }
  public Guid ExecutionLegId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}
