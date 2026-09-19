namespace Client.Models.DTO.Fleet;

public sealed record FuelPriceComparisonMapDto(
  DateOnly Date,
  DateOnly NextDate,
  FuelDiscountMapDto Next,
  decimal RetailChange,
  decimal DiscountChange,
  decimal? IftaChange,
  decimal SavingsChange
)
{
  public decimal? RetailChangePercent { get; init; }
  public decimal? DiscountChangePercent { get; init; }
  public decimal? IftaChangePercent { get; init; }
  public decimal? SavingsChangePercent { get; init; }
}
