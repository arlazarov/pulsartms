using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Services;

public sealed class DispatchRates(IAppDbContext db)
{
  public static bool Matches(DispatchRate saved, Domain.Entities.Dispatch.Dispatch load, decimal? emptyMiles, string connectionHash) =>
    saved.Price == load.Price && saved.LoadedMiles == load.LoadedMiles && saved.EmptyMiles == emptyMiles
    && saved.ConnectionHash == connectionHash && saved.Currency == load.Currency;

  public static decimal? PerMile(decimal? price, decimal? miles) => price is >= 0 && miles is > 0
    ? decimal.Round(price.Value / miles.Value, 6, MidpointRounding.AwayFromZero) : null;

  public async Task SaveAsync(Domain.Entities.Dispatch.Dispatch load,
    decimal? emptyMiles, string connectionHash, CancellationToken ct)
  {
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    await db.LockDispatchRatesAsync(ct);
    var saved = await db.DispatchRates.SingleOrDefaultAsync(x => x.DispatchId == load.Id, ct);
    if (saved is not null && Matches(saved, load, emptyMiles, connectionHash)) return;
    if (saved is null)
    {
      saved = new() { Id = Guid.NewGuid(), DispatchId = load.Id };
      db.DispatchRates.Add(saved);
    }
    saved.Price = load.Price;
    saved.Currency = load.Currency;
    saved.LoadedMiles = load.LoadedMiles;
    saved.EmptyMiles = emptyMiles;
    saved.ConnectionHash = connectionHash;
    saved.LoadedRatePerMile = PerMile(load.Price, load.LoadedMiles);
    saved.TotalRatePerMile = emptyMiles is >= 0 && load.LoadedMiles is > 0
      ? PerMile(load.Price, load.LoadedMiles + emptyMiles) : null;
    saved.CalculatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
  }
}
