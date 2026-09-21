namespace Domain.Models.Fuel;

public sealed record FuelPriceComparisonDto(
  DateOnly Date,
  DateOnly NextDate,
  FuelDiscountDto Next,
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

  public static FuelPriceComparisonDto? Create(
    DateOnly date,
    FuelDiscountDto? current,
    FuelDiscountDto? next
  )
  {
    if (
      date == DateOnly.MaxValue
      || current is null
      || next is null
      || !current.Currency.Equals(
        next.Currency,
        StringComparison.OrdinalIgnoreCase
      )
      || current.Unit != next.Unit
      || current.DiscountPrice <= 0
      || next.DiscountPrice <= 0
      || current.EffectiveFrom > date
      || current.EffectiveTo < date
      || next.EffectiveFrom > date.AddDays(1)
      || next.EffectiveTo < date.AddDays(1)
    )
      return null;
    return new(
      date,
      date.AddDays(1),
      next,
      next.RetailPrice - current.RetailPrice,
      next.DiscountPrice - current.DiscountPrice,
      current.PriceAfterIfta is { } before && next.PriceAfterIfta is { } after
        ? after - before
        : null,
      next.Savings - current.Savings
    )
    {
      RetailChangePercent = Percent(current.RetailPrice, next.RetailPrice),
      DiscountChangePercent = Percent(
        current.DiscountPrice,
        next.DiscountPrice
      ),
      IftaChangePercent = Percent(current.PriceAfterIfta, next.PriceAfterIfta),
      SavingsChangePercent = Percent(current.Savings, next.Savings),
    };
  }

  private static decimal? Percent(decimal? current, decimal? next) =>
    current is > 0 && next.HasValue
      ? (next.Value - current.Value) / current.Value * 100m
      : null;
}
