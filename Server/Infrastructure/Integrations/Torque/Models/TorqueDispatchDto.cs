namespace Infrastructure.Integrations.Torque.Models;

public class TorqueDispatchDto
{
  public int LoadNumber { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public string? OrderDate { get; set; }
  public string? InvoiceDate { get; set; }
  public string? ShipDate { get; set; }
  public string? DeliveryDate { get; set; }
  public string DispatcherName { get; set; } = string.Empty;
  public string CustomerName { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string CarrierName { get; set; } = string.Empty;
  public string TruckNumber { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal? LoadedMiles { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal? TotalCharge { get; set; }
  public List<TorqueDispatchStopDto> Stops { get; set; } = [];
}
