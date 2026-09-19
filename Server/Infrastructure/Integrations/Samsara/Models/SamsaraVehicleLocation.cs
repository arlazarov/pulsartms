namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraVehicleLocation
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public SamsaraEngineState? EngineState { get; set; }
  public SamsaraGps? Gps { get; set; }
  public SamsaraFuelPercent? FuelPercent { get; set; }
  public SamsaraTemperature? AmbientAirTemperatureMilliC { get; set; }
}

public class SamsaraTemperature
{
  public DateTime Time { get; set; }
  public decimal? Value { get; set; }
}

public class SamsaraEngineState
{
  public SamsaraTemperature? AmbientAirTemperatureMilliC { get; set; }
  public DateTime Time { get; set; }
  public string Value { get; set; } = string.Empty;
}

public class SamsaraFuelPercent
{
  public SamsaraTemperature? AmbientAirTemperatureMilliC { get; set; }
  public DateTime Time { get; set; }
  public decimal Value { get; set; }
}

public class SamsaraGps
{
  public SamsaraTemperature? AmbientAirTemperatureMilliC { get; set; }
  public DateTime Time { get; set; }
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public decimal HeadingDegrees { get; set; }
  public decimal SpeedMilesPerHour { get; set; }
  public SamsaraReverseGeo? ReverseGeo { get; set; }
}

public class SamsaraReverseGeo
{
  public string FormattedLocation { get; set; } = string.Empty;
}
