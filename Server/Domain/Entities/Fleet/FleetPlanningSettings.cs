namespace Domain.Entities.Fleet;

public class FleetPlanningSettings : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string SettingsJson { get; set; } = "";
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
