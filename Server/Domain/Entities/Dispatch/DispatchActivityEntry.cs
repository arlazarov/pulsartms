namespace Domain.Entities.Dispatch;

public sealed class DispatchActivityEntry : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public long CreatedRevision { get; set; }
  public long Revision { get; set; }
  public Guid AddOperationId { get; set; }
  public string Kind { get; set; } = "note";
  public string Text { get; set; } = "";
  public Guid? StopId { get; set; }
  public string? StopLabel { get; set; }
  public Guid? DriverId { get; set; }
  public string? DriverName { get; set; }
  public Guid ActorId { get; set; }
  public string ActorName { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public bool NeedsAttention { get; set; }
  public Guid? ResolveOperationId { get; set; }
  public Guid? ResolvedBy { get; set; }
  public string? ResolvedByName { get; set; }
  public DateTime? ResolvedAt { get; set; }
}
