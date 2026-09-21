using Domain.Entities.Fuel;
using Domain.Models.Fuel;

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

              // The carrier pays the program price where a program applies
              // and the known retail price otherwise. With neither known the
              // station has no price and stays out of cost comparison.
              var paid =
                discount.DiscountPrice > 0 ? discount.DiscountPrice
                : discount.RetailPrice > 0 ? discount.RetailPrice
                : 0m;
              return discount with
              {
                Product = product,
                DiscountPrice = paid,
                Savings = discount.RetailPrice - paid,
                Unit = unit,
                PriceAfterIfta =
                  paid <= 0 ? null
                  : sourceVolume.HasValue && targetVolume.HasValue
                    ? paid
                      - iftaRate!.Rate * targetVolume.Value / sourceVolume.Value
                  : oregonDiesel ? paid
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
