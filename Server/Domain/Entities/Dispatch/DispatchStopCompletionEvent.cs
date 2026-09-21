namespace Domain.Entities.Dispatch;

public sealed class DispatchStopCompletionEvent : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid StopId { get; set; }
  public long Revision { get; set; }
  public DateTime? CompletedAt { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid ActorId { get; set; }
}
