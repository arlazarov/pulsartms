namespace Domain.Entities.Dispatch;

public sealed class RouteGeometryChunk : ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid RoutePlanId { get; init; }
  public string Key { get; init; } = "";
  public string CoordinatesJson { get; init; } = "[]";
}
