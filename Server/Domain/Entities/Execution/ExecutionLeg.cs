namespace Domain.Entities.Execution;

public sealed class ExecutionLeg : BaseEntity
{
  public Guid TripId { get; set; }
  public Trip Trip { get; set; } = null!;
  public Guid TruckId { get; set; }
  public Guid? DriverId { get; set; }
  public Guid? CoDriverId { get; set; }
  public Guid? TrailerId { get; set; }
  public string Status { get; set; } = "planned";
  public long Revision { get; set; }
  public long RouteChoiceRevision { get; set; }
  public Guid? StartSwitchId { get; set; }
  public Guid? EndSwitchId { get; set; }
  public DateTime? StartedAt { get; set; }
  public DateTime? CompletedAt { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid? RecordedBy { get; set; }
  public List<ExecutionLegStop> Stops { get; set; } = [];
  public string SourceAssignmentSignature { get; set; } = "";
  public string SourceSignature { get; set; } = "";
  public string SourceObservedSignature { get; set; } = "";
  public string? SourceReviewReason { get; set; }
  public List<LoadExecutionLeg> Loads { get; set; } = [];
}
