namespace Domain.Entities.Dispatch;

public class DispatchRoutePlan : BaseEntity
{
  public Guid DispatchId { get; set; }
  public Guid TruckId { get; set; }
  public string InputHash { get; set; } = "";
  public string PlanJson { get; set; } = "{}";
  public DateTime CreatedAt { get; set; }
}
