namespace Domain.Entities.Dispatch;

public sealed class DispatchRouteChoice : BaseEntity
{
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public string InputHash { get; set; } = "";
  public string ChoiceJson { get; set; } = "";
  public long Revision { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}
