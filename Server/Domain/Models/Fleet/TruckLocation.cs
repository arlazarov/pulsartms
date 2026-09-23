namespace Domain.Models.Fleet;

public class TruckLocation
{
  public Guid TruckId { get; set; }
  public string TruckExternalId { get; set; } = string.Empty;
  public string UnitNumber { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;

  // Where the trailer came from (telemetry, execution or load), and a
  // trailer another source names for this truck when they disagree.
  public string? TrailerSource { get; set; }
  public string TrailerConflictNumber { get; set; } = string.Empty;
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public decimal Speed { get; set; }
  public decimal Heading { get; set; }
  public DateTime UpdatedAt { get; set; }
  public DateTime ObservedAt { get; set; }
  public string FormattedLocation { get; set; } = string.Empty;
  public string EngineState { get; set; } = string.Empty;
  public decimal? FuelPercent { get; set; }
  public DateTime? FuelUpdatedAt { get; set; }
  public decimal? OutsideTemperatureCelsius { get; set; }
  public DateTime? OutsideTemperatureUpdatedAt { get; set; }
}
