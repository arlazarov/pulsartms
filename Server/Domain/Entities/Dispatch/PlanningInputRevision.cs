using Domain.Entities;

namespace Domain.Entities.Dispatch;

public sealed class PlanningInputRevision : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; set; }
  public long Revision { get; set; }
}
