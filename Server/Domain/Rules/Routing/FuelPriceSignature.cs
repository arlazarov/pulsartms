using System.Security.Cryptography;
using System.Text.Json;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class FuelPriceSignature
{
  public static string From(IReadOnlyList<PricedFuelStation> prices) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          prices
            .OrderBy(x => x.Station.StationId)
            .Select(x => new
            {
              x.Station.StationId,
              x.Station.Point,
              x.Station.PriceDate,
              x.CashUsd,
              x.EconomicUsd,
            })
        )
      )
    );
}
