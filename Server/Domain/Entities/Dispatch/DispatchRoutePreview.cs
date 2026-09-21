namespace Domain.Entities.Dispatch;

public sealed class DispatchRoutePreview : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid PreviewId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public DateTime ExpiresAt { get; set; }
  public string DraftJson { get; set; } = "";
}
