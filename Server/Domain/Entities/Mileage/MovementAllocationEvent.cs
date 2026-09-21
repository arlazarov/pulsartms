namespace Domain.Entities.Mileage;

public sealed class MovementAllocationEvent : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid MovementId { get; init; }
  public long Revision { get; init; }
  public Guid? PreviousAllocationDispatchId { get; init; }
  public Guid? AllocatedDispatchId { get; init; }
  public string Target { get; init; } = "unallocated";
  public string Reason { get; init; } = "";
  public long PolicyRevision { get; init; }
  public bool ManualOverride { get; init; }
  public DateTime RecordedAt { get; init; }
  public Guid RecordedBy { get; init; }
}
