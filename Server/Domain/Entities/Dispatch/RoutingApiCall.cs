namespace Domain.Entities.Dispatch;

public class RoutingApiCall : BaseEntity
{
  public string RequestHash { get; set; } = "";
  public string Operation { get; set; } = "";
  public DateTime CreatedAt { get; set; }
  public DateTime ExpiresAt { get; set; }
  public string? ResultJson { get; set; }
  public string? ErrorMessage { get; set; }
}
