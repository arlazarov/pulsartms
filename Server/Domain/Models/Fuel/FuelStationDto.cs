namespace Domain.Models.Fuel;

// A fuel station and what it costs there: the retail price, the price this
// carrier pays, and what that is after IFTA. The rules that pick where to
// fuel are given these, so they live here rather than with the query that
// happens to read them.
public record FuelStationDto(
  Guid Id,
  string ExternalId,
  string Name,
  string Address,
  string City,
  string Region,
  string PostalCode,
  string Country,
  decimal? Latitude,
  decimal? Longitude,
  List<FuelDiscountDto> Discounts
)
{
  public FuelDiscountDto? CashDiscount { get; init; }
  public FuelDiscountDto? IftaDiscount { get; init; }
  public FuelPriceComparisonDto? CashComparison { get; init; }
  public FuelPriceComparisonDto? IftaComparison { get; init; }

  // The same comparison one day back: yesterday against the day asked for.
  public FuelPriceComparisonDto? CashPreviousComparison { get; init; }
  public FuelPriceComparisonDto? IftaPreviousComparison { get; init; }
}

public record FuelDiscountDto(
  string Currency,
  string Product,
  decimal RetailPrice,
  decimal DiscountPrice,
  decimal Savings,
  DateOnly EffectiveFrom,
  DateOnly EffectiveTo,
  decimal? PriceAfterIfta,
  string Unit = ""
);
