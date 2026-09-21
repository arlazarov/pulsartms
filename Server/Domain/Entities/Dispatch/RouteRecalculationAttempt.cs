namespace Domain.Entities.Dispatch;

public sealed class RouteRecalculationAttempt : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; set; }
  public DateTime CreatedAt { get; set; }
  public double Latitude { get; set; }
  public double Longitude { get; set; }
}
