namespace Client.Models.DTO.Fleet;

public class FuelDiscountMapDto
{
  public string Unit { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public string Product { get; set; } = string.Empty;
  public decimal RetailPrice { get; set; }
  public decimal DiscountPrice { get; set; }
  public decimal? PriceAfterIfta { get; set; }
  public decimal Savings { get; set; }
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly EffectiveTo { get; set; }
}
