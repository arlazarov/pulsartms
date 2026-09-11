namespace Domain.Entities.Fleet;

public class FleetPlanningSettings : BaseEntity
{
  public string SettingsJson { get; set; } = "";
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
