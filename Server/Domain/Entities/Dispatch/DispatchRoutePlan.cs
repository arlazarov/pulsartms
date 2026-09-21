namespace Domain.Entities.Dispatch;

public class DispatchRoutePlan : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public Guid TruckId { get; set; }
  public string InputHash { get; set; } = "";
  public string PlanJson { get; set; } = "{}";
  public DateTime CreatedAt { get; set; }
}
