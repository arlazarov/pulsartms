using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Fuel.Queries.GetFuelStations;

namespace Application.Features.Routing.Algorithms;

public static class UsFuelDiscountSignature
{
  public static string Calendar(
    IReadOnlyDictionary<DateOnly, List<FuelStationDto>> days
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          days.OrderBy(x => x.Key)
            .Select(x => new { Date = x.Key, Signature = From(x.Value, x.Key) })
        )
      )
    );

  public static string From(
    IEnumerable<FuelStationDto> stations,
    DateOnly date
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          stations
            .SelectMany(station =>
              FuelDisplayPrices
                .EligibleQuotes(station.Discounts, date)
                .Where(quote =>
                  quote.Currency.Equals(
                    "USD",
                    StringComparison.OrdinalIgnoreCase
                  )
                )
                .Select(quote => new
                {
                  station.Id,
                  station.Latitude,
                  station.Longitude,
                  quote.EffectiveFrom,
                  quote.EffectiveTo,
                  quote.DiscountPrice,
                })
            )
            .Distinct()
            .OrderBy(x => x.Id)
            .ThenBy(x => x.EffectiveFrom)
            .ThenBy(x => x.EffectiveTo)
            .ThenBy(x => x.DiscountPrice)
        )
      )
    );
}
