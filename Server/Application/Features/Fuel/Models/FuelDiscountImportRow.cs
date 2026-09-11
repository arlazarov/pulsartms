namespace Application.Features.Fuel.Models;

public class FuelDiscountImportRow
{
  public string StationId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string State { get; set; } = string.Empty;
  public decimal RetailPrice { get; set; }
  public decimal DiscountPrice { get; set; }
}
