using Domain.Entities.Fuel;

namespace Application.Features.Fuel.Queries.GetFuelStations;

internal static class FuelPriceCalculator
{
  public static List<FuelStationDto> ApplyIfta(
    IReadOnlyCollection<FuelStationDto> stations,
    IReadOnlyCollection<IftaTaxRate> iftaRates
  )
  {
    var rates = new Dictionary<(string Region, string Currency), IftaTaxRate>();

    foreach (var rate in iftaRates)
    {
      rates.TryAdd(
        (
          rate.Jurisdiction.ToUpperInvariant(),
          rate.Currency.ToUpperInvariant()
        ),
        rate
      );
    }

    return
    [
      .. stations.Select(station =>
        station with
        {
          Discounts =
          [
            .. station.Discounts.Select(discount =>
            {
              var hasIftaRate = rates.TryGetValue(
                (
                  station.Region.ToUpperInvariant(),
                  discount.Currency.ToUpperInvariant()
                ),
                out var iftaRate
              );

              var unit = discount.Currency.Trim().ToUpperInvariant() switch
              {
                "CAD" => "L",
                "USD" => "US gal",
                _ => "",
              };
              var sourceVolume = hasIftaRate
                ? LitresPerUnit(iftaRate!.Unit)
                : null;
              var targetVolume = LitresPerUnit(unit);
              var product = string.IsNullOrWhiteSpace(discount.Product)
                ? "Diesel"
                : discount.Product;
              // Oregon weight-mile diesel has no per-gallon IFTA deduction;
              // unknown rates elsewhere stay unknown.
              var oregonDiesel =
                !hasIftaRate
                && station
                  .Region.Trim()
                  .Equals("OR", StringComparison.OrdinalIgnoreCase)
                && discount
                  .Currency.Trim()
                  .Equals("USD", StringComparison.OrdinalIgnoreCase)
                && product
                  .Trim()
                  .Equals("Diesel", StringComparison.OrdinalIgnoreCase);

              return discount with
              {
                Product = product,
                Savings = discount.RetailPrice - discount.DiscountPrice,
                Unit = unit,
                PriceAfterIfta =
                  sourceVolume.HasValue && targetVolume.HasValue
                    ? discount.DiscountPrice
                      - iftaRate!.Rate * targetVolume.Value / sourceVolume.Value
                  : oregonDiesel ? discount.DiscountPrice
                  : null,
              };
            }),
          ],
        }
      ),
    ];
  }

  private static decimal? LitresPerUnit(string unit) =>
    unit.Trim().ToUpperInvariant() switch
    {
      "L" or "LITER" or "LITERS" or "LITRE" or "LITRES" => 1m,
      "G"
      or "GAL"
      or "GALLON"
      or "GALLONS"
      or "US GAL"
      or "US GALLON"
      or "US GALLONS" => 3.785411784m,
      _ => null,
    };
}
