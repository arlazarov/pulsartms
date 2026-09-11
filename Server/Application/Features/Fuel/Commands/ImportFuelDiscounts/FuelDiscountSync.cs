using Application.Features.Fuel.Models;
using Domain.Entities.Fuel;

namespace Application.Features.Fuel.Commands.ImportFuelDiscounts;

public static class FuelDiscountSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    FuelDiscountImportData import,
    CancellationToken cancellationToken = default
  )
  {
    var stationIds = import
      .Rows.Select(x => x.StationId)
      .Where(x => !string.IsNullOrWhiteSpace(x))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var stations = await dbContext
      .FuelStations.Where(x => stationIds.Contains(x.ExternalId))
      .ToDictionaryAsync(x => x.ExternalId, cancellationToken);

    var fuelStationIds = stations.Values.Select(x => x.Id).ToList();

    var discounts = await dbContext
      .FuelDiscounts.Where(x =>
        fuelStationIds.Contains(x.FuelStationId)
        && x.Currency == import.Currency
        && x.EffectiveFrom == import.EffectiveDate
      )
      .ToDictionaryAsync(x => x.FuelStationId, cancellationToken);

    foreach (var row in import.Rows)
    {
      if (!stations.TryGetValue(row.StationId, out var station))
      {
        continue;
      }

      if (!discounts.TryGetValue(station.Id, out var discount))
      {
        discount = new FuelDiscount
        {
          Id = Guid.NewGuid(),
          FuelStationId = station.Id,
          Currency = import.Currency,
          Product = "Diesel",
          Savings = row.RetailPrice - row.DiscountPrice,
          RetailPrice = row.RetailPrice,
          DiscountPrice = row.DiscountPrice,
          EffectiveFrom = import.EffectiveDate,
          EffectiveTo = import.EffectiveTo ?? import.EffectiveDate,
        };

        dbContext.FuelDiscounts.Add(discount);
        discounts.Add(station.Id, discount);
        continue;
      }

      discount.EffectiveTo = import.EffectiveTo ?? import.EffectiveDate;
      discount.Product = "Diesel";
      discount.Savings = row.RetailPrice - row.DiscountPrice;
      discount.RetailPrice = row.RetailPrice;
      discount.DiscountPrice = row.DiscountPrice;
    }
  }
}
