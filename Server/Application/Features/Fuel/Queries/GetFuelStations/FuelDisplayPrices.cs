namespace Application.Features.Fuel.Queries.GetFuelStations;

public static class FuelDisplayPrices
{
  public static IEnumerable<FuelDiscountDto> EligibleQuotes(IEnumerable<FuelDiscountDto> discounts, DateOnly date) =>
    discounts.Where(price => price.EffectiveFrom <= date && price.EffectiveTo >= date
      && price.DiscountPrice > 0
      && price.Product.Contains("diesel", StringComparison.OrdinalIgnoreCase)
      && !price.Product.Contains("reefer", StringComparison.OrdinalIgnoreCase)
      && (price.Currency.Equals("CAD", StringComparison.OrdinalIgnoreCase)
        ? price.Unit is "" or "L"
        : price.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase) && price.Unit is "" or "US gal"));

  public static FuelStationDto Select(FuelStationDto station, DateOnly date)
  {
    var quotes = EligibleQuotes(station.Discounts, date).ToList();
    // Map reads have no truck FX profile; unlike-unit currencies are not comparable.
    if (quotes.Select(price => price.Currency.ToUpperInvariant()).Distinct().Take(2).Count() != 1)
      return station with { CashDiscount = null, IftaDiscount = null };

    return station with
    {
      CashDiscount = quotes.OrderBy(price => price.DiscountPrice)
        .ThenByDescending(price => price.EffectiveFrom).ThenBy(price => price.Product, StringComparer.Ordinal).First(),
      IftaDiscount = quotes.Where(price => price.PriceAfterIfta is > 0).OrderBy(price => price.PriceAfterIfta)
        .ThenByDescending(price => price.EffectiveFrom).ThenBy(price => price.Product, StringComparer.Ordinal).FirstOrDefault(),
    };
  }
}
