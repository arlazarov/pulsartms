namespace Domain.Entities.Execution;

public sealed class SwitchParticipant : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid SwitchId { get; set; }
  public DispatchSwitchOperation Switch { get; set; } = null!;
  public Guid DispatchId { get; set; }
  public Guid OutgoingLegId { get; set; }
  public Guid IncomingLegId { get; set; }
  public Guid ReleaseVisitId { get; set; }
  public Guid ReceiveVisitId { get; set; }
  public string TransferKind { get; set; } = "resource_handoff";
  public DateTime? PlannedReleaseAt { get; set; }
  public DateTime? PlannedReceiveAt { get; set; }
  public DateTime? ReleasedAt { get; set; }
  public DateTime? ReceivedAt { get; set; }
  public Guid? ReleasedBy { get; set; }
  public Guid? ReceivedBy { get; set; }
  public long Revision { get; set; }
  public bool IsCancelled { get; set; }
  public string OutgoingRestoreJson { get; set; } = "";
}
