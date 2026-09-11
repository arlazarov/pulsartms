namespace Domain.Entities.Fleet;

public class Truck : BaseEntity
{
  public string ExternalId { get; set; } = string.Empty;
  public string UnitNumber { get; set; } = string.Empty;
  public string Vin { get; set; } = string.Empty;
  public bool IsActive { get; set; }
  public Guid? DriverId { get; set; }
  public Driver? Driver { get; set; }
  public Guid? TrailerId { get; set; }
  public Trailer? Trailer { get; set; }
}
