namespace Domain.Entities.Dispatch;

public sealed class DispatchBaseRoute : BaseEntity
{
  public Guid DispatchId { get; set; }
  public string InputHash { get; set; } = "";
  public string RouteJson { get; set; } = "";
  public DateTime CalculatedAt { get; set; }
}
