namespace Domain.Entities.Dispatch;

public sealed class DispatchBaseRoute : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public string InputHash { get; set; } = "";
  public string RouteJson { get; set; } = "";
  public DateTime CalculatedAt { get; set; }

  // Raised by every write of RouteJson. The row is rewritten in place, so
  // this - not the id, the input hash or the calculation time, which two
  // alternatives of one provider answer can share - names the road a copy
  // of it was taken from.
  public long Revision { get; set; }
}
