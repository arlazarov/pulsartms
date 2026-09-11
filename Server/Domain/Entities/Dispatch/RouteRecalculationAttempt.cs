namespace Domain.Entities.Dispatch;

public sealed class RouteRecalculationAttempt : BaseEntity
{
  public Guid TruckId { get; set; }
  public DateTime CreatedAt { get; set; }
  public double Latitude { get; set; }
  public double Longitude { get; set; }
}
