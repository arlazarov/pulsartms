using Domain.Entities.Fuel;

namespace Application.Features.Fuel.Commands.SyncIftaTaxRates;

public static class IftaTaxRateSync
{
  public static async Task SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyCollection<IftaTaxRateData> rates,
    int year,
    int quarter,
    CancellationToken cancellationToken = default
  )
  {
    var effectiveFrom = new DateOnly(year, (quarter - 1) * 3 + 1, 1);
    var effectiveTo = effectiveFrom.AddMonths(3).AddDays(-1);

    var existing = await dbContext
      .IftaTaxRates.Where(x => x.EffectiveFrom == effectiveFrom && x.EffectiveTo == effectiveTo)
      .ToListAsync(cancellationToken);

    foreach (var rate in rates)
    {
      var current = existing.FirstOrDefault(x =>
        x.Jurisdiction.Equals(rate.Jurisdiction, StringComparison.OrdinalIgnoreCase)
        && x.FuelType.Equals(rate.FuelType, StringComparison.OrdinalIgnoreCase)
      );

      if (current is null)
      {
        dbContext.IftaTaxRates.Add(
          new IftaTaxRate
          {
            Id = Guid.NewGuid(),
            Jurisdiction = rate.Jurisdiction,
            FuelType = rate.FuelType,
            Rate = rate.Rate,
            Currency = rate.Currency,
            Unit = rate.Unit,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
          }
        );

        continue;
      }

      current.Rate = rate.Rate;
      current.Currency = rate.Currency;
      current.Unit = rate.Unit;
    }
  }
}
