namespace Domain.Entities.Execution;

public sealed class LoadExecutionLeg : BaseEntity
{
  public Guid DispatchId { get; set; }
  public Guid ExecutionLegId { get; set; }
  public ExecutionLeg ExecutionLeg { get; set; } = null!;
  public int Sequence { get; set; }
  public Guid StartVisitId { get; set; }
  public Guid EndVisitId { get; set; }
}
