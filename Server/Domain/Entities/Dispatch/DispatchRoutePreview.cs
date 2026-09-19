namespace Domain.Entities.Dispatch;

public sealed class DispatchRoutePreview : BaseEntity
{
  public Guid PreviewId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public DateTime ExpiresAt { get; set; }
  public string DraftJson { get; set; } = "";
}
