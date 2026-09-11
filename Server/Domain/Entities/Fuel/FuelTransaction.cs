namespace Domain.Entities.Fuel;

public class FuelTransaction : BaseEntity
{
  public Guid FuelStationId { get; set; }

  public FuelStation FuelStation { get; set; } = default!;

  public string ExternalTransactionId { get; set; } = string.Empty;

  public string TruckNumber { get; set; } = string.Empty;

  public string DriverName { get; set; } = string.Empty;

  public DateTime TransactionDate { get; set; }

  public string Product { get; set; } = string.Empty;

  public decimal Quantity { get; set; }

  public decimal UnitPrice { get; set; }

  public decimal TotalAmount { get; set; }

  public string Currency { get; set; } = string.Empty;
}
