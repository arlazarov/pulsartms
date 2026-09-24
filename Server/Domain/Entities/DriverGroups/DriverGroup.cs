namespace Domain.Entities.DriverGroups;

// A dispatcher's own named set of drivers ("West", "Local"), used only to
// narrow what the program lists; it grants and removes no access. It
// belongs to one user in one company; another dispatcher neither sees nor
// changes it. Groups may share drivers.
public sealed class DriverGroup : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid OwnerUserId { get; set; }
  public string Name { get; set; } = "";
  public long Revision { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public sealed class DriverGroupMember : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid GroupId { get; set; }
  public Guid DriverId { get; set; }
}
