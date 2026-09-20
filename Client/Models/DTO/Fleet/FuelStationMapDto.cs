namespace Client.Models.DTO.Fleet;

public class FuelStationMapDto
{
  public Guid Id { get; set; }
  public string ExternalId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string City { get; set; } = string.Empty;
  public string Region { get; set; } = string.Empty;
  public string Country { get; set; } = string.Empty;
  public string Address { get; set; } = string.Empty;
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  public List<FuelDiscountMapDto> Discounts { get; set; } = [];
  public FuelDiscountMapDto? CashDiscount { get; set; }
  public FuelDiscountMapDto? IftaDiscount { get; set; }
  public FuelPriceComparisonMapDto? CashComparison { get; set; }
  public FuelPriceComparisonMapDto? IftaComparison { get; set; }
  public FuelPriceComparisonMapDto? CashPreviousComparison { get; set; }
  public FuelPriceComparisonMapDto? IftaPreviousComparison { get; set; }
}
