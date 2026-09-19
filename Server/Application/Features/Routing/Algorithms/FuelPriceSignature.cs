using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

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
