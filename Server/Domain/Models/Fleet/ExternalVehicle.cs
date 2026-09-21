namespace Domain.Models.Fleet;

public class ExternalVehicle
{
  public string ExternalId { get; set; } = string.Empty;
  public string UnitNumber { get; set; } = string.Empty;
  public string Vin { get; set; } = string.Empty;
  public bool IsActive { get; set; }
}
