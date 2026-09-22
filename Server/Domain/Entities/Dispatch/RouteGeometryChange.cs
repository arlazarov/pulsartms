namespace Domain.Entities.Dispatch;

public sealed class RouteGeometryChange : ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid RoutePlanId { get; init; }
  public long Revision { get; init; }
  public DateTime RecordedAt { get; init; }
  public string ChangesJson { get; init; } = "{}";
}
