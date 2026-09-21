namespace Domain.Entities.Dispatch;

public sealed class DispatchActivityThread : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public long Revision { get; set; }
}
