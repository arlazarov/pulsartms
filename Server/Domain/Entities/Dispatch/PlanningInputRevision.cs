namespace Domain.Entities.Dispatch;

public sealed class PlanningInputRevision
{
  public Guid TruckId { get; set; }
  public long Revision { get; set; }
}
