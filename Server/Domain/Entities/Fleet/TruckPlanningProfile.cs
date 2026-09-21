namespace Domain.Entities.Fleet;

public class TruckPlanningProfile : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid TruckId { get; set; }
  public string SettingsJson { get; set; } = "{}";
}
