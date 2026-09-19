namespace Domain.Entities.Execution;

public sealed class ExecutionPlanningChange
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid DispatchId { get; set; }
  public Guid TruckId { get; set; }
  public Guid ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public DateTime RequestedAt { get; set; }
  public DateTime AvailableAt { get; set; }
  public DateTime? CompletedAt { get; set; }
  public Guid? LeaseId { get; set; }
  public DateTime? LeaseUntil { get; set; }
  public int Attempts { get; set; }
  public bool MileageOnly { get; set; }
}
