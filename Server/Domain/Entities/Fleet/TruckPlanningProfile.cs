namespace Domain.Entities.Fleet;

public class TruckPlanningProfile : BaseEntity
{
  public Guid TruckId { get; set; }
  public string SettingsJson { get; set; } = "{}";
}
