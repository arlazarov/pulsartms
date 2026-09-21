namespace Domain.Entities.Mileage;

public sealed class Movement : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid IdempotencyKey { get; init; }
  public string RequestHash { get; init; } = "";
  public Guid TruckId { get; init; }
  public Guid? DriverId { get; init; }
  public Guid? CoDriverId { get; init; }
  public Guid? TrailerId { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public string Origin { get; init; } = "manual";
  public Guid? FromVisitId { get; init; }
  public Guid? ToVisitId { get; init; }
  public bool PlannedSuperseded { get; set; }
  public string Purpose { get; init; } = "reposition";
  public string CargoState { get; init; } = "unknown";
  public Guid? PreviousDispatchId { get; init; }
  public Guid? NextDispatchId { get; init; }
  public Guid? CarriedDispatchId { get; init; }
  public string FromLocation { get; init; } = "";
  public string ToLocation { get; init; } = "";
  public DateTime? StartedAt { get; set; }
  public DateTime? EndedAt { get; set; }
  public DateTime RecordedAt { get; init; }
  public Guid RecordedBy { get; init; }
  public long Revision { get; set; }
  public decimal? PlannedMiles { get; set; }
  public decimal? ActualMiles { get; set; }
  public Guid? PlannedEvidenceId { get; set; }
  public Guid? ActualEvidenceId { get; set; }
  public DateTime? PlannedAt { get; set; }
  public DateTime? ActualAt { get; set; }
  public string? PlannedSource { get; set; }
  public string? PlannedSourceReference { get; set; }
  public string? ActualSource { get; set; }
  public string? ActualSourceReference { get; set; }
  public Guid? AllocatedDispatchId { get; set; }
  public string AllocationTarget { get; set; } = "unallocated";
  public string AllocationReason { get; set; } = "no-context";
  public long PolicyRevision { get; set; }
  public bool ManualOverride { get; set; }
}
