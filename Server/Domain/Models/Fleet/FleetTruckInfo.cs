namespace Domain.Models.Fleet;

public class FleetTruckInfo
{
  public Guid TruckId { get; set; }
  public string TruckExternalId { get; set; } = string.Empty;
  public string UnitNumber { get; set; } = string.Empty;
  public bool IsActive { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
}
