using Application.Features.Fuel.Queries.GetFuelStations;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPriceComparisonTests
{
  private static readonly DateOnly Date = new(2026, 9, 10);

  [Theory]
  [InlineData(-1)]
  [InlineData(0)]
  public void UsesSelectedDateAndFollowingDateWithServerCalculatedDifferences(
    int offset
  )
  {
    var date = Date.AddDays(offset);
    var current = Quote(date, 5m, 4.5m);
    var next = Quote(date.AddDays(1), 4.8m, 4.4m);
    var result = FuelPriceComparisonDto.Create(date, current, next)!;
    Assert.Equal(date, result.Date);
    Assert.Equal(date.AddDays(1), result.NextDate);
    Assert.Equal(-.2m, result.DiscountChange);
    Assert.Equal(-.1m, result.IftaChange);
    Assert.Equal(0m, result.SavingsChange);
    Assert.Equal(-4m, result.DiscountChangePercent);
    Assert.Equal(0m, result.SavingsChangePercent);
    Assert.Equal((4.4m - 4.5m) / 4.5m * 100m, result.IftaChangePercent);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("currency")]
  [InlineData("unit")]
  [InlineData("expired")]
  [InlineData("future")]
  public void MissingOrUnlikeQuotesAreNotCompared(string condition)
  {
    var next = Quote(Date.AddDays(1), 4m, 3m);
    next = condition switch
    {
      "missing" => null,
      "currency" => next with { Currency = "CAD" },
      "unit" => next with { Unit = "L" },
      "expired" => next with { EffectiveTo = Date },
      "future" => next with { EffectiveFrom = Date.AddDays(2) },
      _ => next,
    };
    Assert.Null(FuelPriceComparisonDto.Create(Date, Quote(Date, 5m, 4m), next));
  }

  [Fact]
  public void UnavailableIftaStaysUnknownAndIdenticalPricesHaveZeroChange()
  {
    var current = Quote(Date, 5m, null);
    var next = Quote(Date.AddDays(1), 5m, 4m);
    var result = FuelPriceComparisonDto.Create(Date, current, next)!;
    Assert.Equal(0m, result.DiscountChange);
    Assert.Null(result.IftaChange);
    Assert.Null(result.IftaChangePercent);
    Assert.Equal(0m, result.DiscountChangePercent);
  }

  [Fact]
  public void ZeroBaselineHasNoPercentageAndIncreasesUseTheOriginalQuote()
  {
    var current = Quote(Date, 4m, 0m) with { RetailPrice = 0, Savings = 0 };
    var next = Quote(Date.AddDays(1), 5m, 4m);
    var result = FuelPriceComparisonDto.Create(Date, current, next)!;
    Assert.Equal(25m, result.DiscountChangePercent);
    Assert.Null(result.RetailChangePercent);
    Assert.Null(result.IftaChangePercent);
    Assert.Null(result.SavingsChangePercent);
  }

  private static FuelDiscountDto Quote(
    DateOnly date,
    decimal price,
    decimal? ifta
  ) =>
    new("USD", "Diesel", price + .5m, price, .5m, date, date, ifta, "US gal");
}
