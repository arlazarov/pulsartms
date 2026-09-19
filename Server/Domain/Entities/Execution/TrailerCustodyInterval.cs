namespace Domain.Entities.Execution;

public sealed class TrailerCustodyInterval : BaseEntity
{
  public Guid TrailerId { get; set; }
  public Guid ParticipantId { get; set; }
  public Guid ReleaseVisitId { get; set; }
  public Guid ReceiveVisitId { get; set; }
  public DateTime? ReleasedAt { get; set; }
  public DateTime? ReceivedAt { get; set; }
  public Guid ReleasedBy { get; set; }
  public Guid? ReceivedBy { get; set; }
  public long Revision { get; set; }
}
