using Domain.Entities.Fleet;

namespace Domain.Entities.Dispatch;

public class Dispatch : BaseEntity
{
  public int LoadNumber { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public DateOnly? OrderDate { get; set; }
  public DateOnly? InvoiceDate { get; set; }
  public DateOnly? ShipDate { get; set; }
  public DateOnly? DeliveryDate { get; set; }
  public Guid? CustomerId { get; set; }
  public Customer? Customer { get; set; }
  public string CustomerName { get; set; } = string.Empty;
  public Guid? DriverId { get; set; }
  public Driver? Driver { get; set; }
  public string DriverName { get; set; } = string.Empty;
  public string CarrierName { get; set; } = string.Empty;
  public Guid? TruckId { get; set; }
  public Truck? Truck { get; set; }
  public string TruckNumber { get; set; } = string.Empty;
  public Guid? TrailerId { get; set; }
  public Trailer? Trailer { get; set; }
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal? LoadedMiles { get; set; }
  public decimal? Price { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTime LastSyncedAt { get; set; }
  public List<DispatchStop> Stops { get; set; } = [];
}
