namespace Domain.Entities.Dispatch;

public sealed class RouteMovementChunk : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid RoutePlanId { get; init; }
  public Guid TruckId { get; init; }
  public DateTime From { get; init; }
  public DateTime To { get; init; }
  public string MovementJson { get; init; } = "{}";
}
