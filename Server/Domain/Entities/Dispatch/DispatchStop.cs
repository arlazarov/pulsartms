using Domain.Entities.Fleet;

namespace Domain.Entities.Dispatch;

public class DispatchStop : BaseEntity
{
  public Guid DispatchId { get; set; }
  public Dispatch Dispatch { get; set; } = null!;
  public int Sequence { get; set; }
  public string Job { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string Province { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
  public string ZipCode { get; set; } = string.Empty;
  public string SourceAddressJson { get; set; } = string.Empty;
  public DateTime? AddressVerifiedAt { get; set; }
  public DateTime? AddressRetryAfter { get; set; }
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public Guid? DriverId { get; set; }
  public Driver? Driver { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public Guid? CoDriverId { get; set; }
  public Driver? CoDriver { get; set; }
  public string CoDriverName { get; set; } = string.Empty;
  public string CarrierName { get; set; } = string.Empty;
  public Guid? TruckId { get; set; }
  public Truck? Truck { get; set; }
  public string TruckNumber { get; set; } = string.Empty;
  public Guid? TrailerId { get; set; }
  public Trailer? Trailer { get; set; }
  public string TrailerNumber { get; set; } = string.Empty;
  public string Commodity { get; set; } = string.Empty;
  public string Notes { get; set; } = string.Empty;
  public string StopNo { get; set; } = string.Empty;
  public decimal? Weight { get; set; }
  public string WeightUnit { get; set; } = string.Empty;
  public decimal? Pieces { get; set; }
  public decimal? Pallets { get; set; }
  public string Temperature { get; set; } = string.Empty;
  public string TemperatureUnit { get; set; } = string.Empty;
  public DateOnly? ScheduledDate { get; set; }
  public TimeOnly? ScheduledTime { get; set; }
  public DateOnly? ScheduledDate2 { get; set; }
  public TimeOnly? ScheduledTime2 { get; set; }
  public bool IsWindow { get; set; }
  public DateTime? ArrivedAt { get; set; }
  public DateTime? PickedUpAt { get; set; }
  public DateTime? DeliveredAt { get; set; }
  public DateTime? DepartedAt { get; set; }
}
