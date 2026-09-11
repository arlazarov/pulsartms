namespace Client.Models.DTO.Dispatch;

public class DispatchResponse
{
  public Client.Models.DTO.Planning.DispatchEta? Eta { get; set; }
  public Guid Id { get; set; }
  public Guid? TruckId { get; set; }
  public int LoadNumber { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public DateOnly? OrderDate { get; set; }
  public DateOnly? InvoiceDate { get; set; }
  public DateOnly? ShipDate { get; set; }
  public DateOnly? DeliveryDate { get; set; }
  public string CustomerName { get; set; } = string.Empty;
  public string DriverName { get; set; } = string.Empty;
  public string TruckNumber { get; set; } = string.Empty;
  public string TrailerNumber { get; set; } = string.Empty;
  public decimal? LoadedMiles { get; set; }
  public decimal? EmptyMiles { get; set; }
  public decimal? TotalMiles { get; set; }
  public decimal? LoadedRatePerMile { get; set; }
  public decimal? TotalRatePerMile { get; set; }
  public string EmptyMilesStatus { get; set; } = "unavailable";
  public decimal? Price { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTime LastSyncedAt { get; set; }
  public List<DispatchStopResponse> Stops { get; set; } = [];
}
