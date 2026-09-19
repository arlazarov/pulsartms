namespace Domain.Entities.Execution;

public sealed class ExecutionLegRevision
{
  public Guid ExecutionLegId { get; init; }
  public long Revision { get; init; }
  public Guid TruckId { get; init; }
  public int SchemaVersion { get; init; } = 1;
  public DateTime RecordedAt { get; init; }
  public Guid? RecordedBy { get; init; }
  public string Operation { get; init; } = "";
  public Guid? CorrelationId { get; init; }
  public string SnapshotJson { get; init; } = "";
}
