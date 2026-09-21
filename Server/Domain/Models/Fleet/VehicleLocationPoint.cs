namespace Domain.Models.Fleet;

public class VehicleLocationPoint
{
  public string ExternalId { get; set; } = string.Empty;
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public decimal Speed { get; set; }
  public decimal Heading { get; set; }
  public DateTime UpdatedAt { get; set; }
  public string FormattedLocation { get; set; } = string.Empty;
}
