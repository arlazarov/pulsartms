namespace Domain.Entities.Fleet;

// The last position observed for one truck, kept so an instance that does not
// collect telemetry can still draw the map and a restart does not start blind.
// GPS, fuel and temperature carry their own observation times because the
// provider reports them independently; one being stale does not age the rest.
public sealed class TruckLocationReading
{
  public Guid TruckId { get; set; }
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public decimal Speed { get; set; }
  public decimal Heading { get; set; }
  public string EngineState { get; set; } = "";
  public string FormattedLocation { get; set; } = "";
  public string TrailerNumber { get; set; } = "";

  // When the provider observed the position.
  public DateTime ObservedAt { get; set; }
  public decimal? FuelPercent { get; set; }
  public DateTime? FuelObservedAt { get; set; }
  public decimal? OutsideTemperatureCelsius { get; set; }
  public DateTime? TemperatureObservedAt { get; set; }
  public DateTime RecordedAt { get; set; }
}
