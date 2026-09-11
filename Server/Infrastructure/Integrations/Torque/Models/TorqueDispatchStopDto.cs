namespace Infrastructure.Integrations.Torque.Models;

public class TorqueDispatchStopDto
{
  public int Sequence { get; set; }
  public string Job { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string Province { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
  public string ZipCode { get; set; } = string.Empty;
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public string CoDriverName { get; set; } = string.Empty;
  public string CarrierName { get; set; } = string.Empty;
  public string TruckNumber { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public string Commodity { get; set; } = string.Empty;
  public string Notes { get; set; } = string.Empty;
  [System.Text.Json.Serialization.JsonConverter(typeof(TorqueScalarTextConverter))]
  public string StopNo { get; set; } = string.Empty;
  public decimal? Weight { get; set; }
  public string WeightUnit { get; set; } = string.Empty;
  public decimal? Pieces { get; set; }
  public decimal? Pallets { get; set; }
  [System.Text.Json.Serialization.JsonConverter(typeof(TorqueScalarTextConverter))]
  public string Temperature { get; set; } = string.Empty;
  public string TemperatureUnit { get; set; } = string.Empty;
  public TorqueScheduledDto? Scheduled { get; set; }
  public TorqueActualDto? Actual { get; set; }
}

public class TorqueScheduledDto
{
  public string PickupDate { get; set; } = string.Empty;
  public string PickupTime { get; set; } = string.Empty;
  public string PickupDate2 { get; set; } = string.Empty;
  public string PickupTime2 { get; set; } = string.Empty;
  public bool IsWindow { get; set; }
}

public class TorqueActualDto
{
  public DateTime? Arrived { get; set; }
  public DateTime? PickedUp { get; set; }
  public DateTime? Delivered { get; set; }
  public DateTime? Departed { get; set; }
}
