using Application.Caching;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;

namespace Application.Features.Fuel.Services;

public sealed class FuelExchangeRateService(
  IFuelExchangeRateStore store,
  IFuelExchangeRateProvider provider,
  ReadCache reads,
  TimeProvider clock
)
{
  private const string CacheGroup = "fuel-exchange-rate";

  public async Task<FuelExchangeRate?> ReadAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var rate = await reads.GetAsync(
      CacheGroup,
      "current",
      () => store.ReadAsync(ct),
      TimeSpan.FromMinutes(1)
    );
    return Valid(rate, clock.GetUtcNow().UtcDateTime) ? rate : null;
  }

  public async Task<FuelExchangeRate?> ReadUncachedAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var rate = await store.ReadAsync(ct);
    return Valid(rate, clock.GetUtcNow().UtcDateTime) ? rate : null;
  }

  public async Task<bool> RefreshAsync(CancellationToken ct)
  {
    var now = clock.GetUtcNow().UtcDateTime;
    if (Recent(await store.ReadAsync(ct), now))
      return false;
    var owner = Guid.NewGuid().ToString("N");
    if (!await store.AcquireAsync(owner, now, ct))
      return false;
    try
    {
      now = clock.GetUtcNow().UtcDateTime;
      var previous = await store.ReadAsync(ct);
      if (Recent(previous, now))
        return false;
      var rate = await provider.ReadAsync(ct);
      now = clock.GetUtcNow().UtcDateTime;
      if (!Valid(rate, now))
        throw new InvalidDataException(
          "The fuel exchange rate is invalid or out of date."
        );
      if (
        Valid(previous, now)
        && (
          rate.ObservedOn < previous!.ObservedOn
          || rate.RetrievedAt <= previous.RetrievedAt
        )
      )
        return false;
      await store.SaveAsync(owner, rate, ct);
      reads.Invalidate(CacheGroup);
      return true;
    }
    finally
    {
      using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      await store.ReleaseAsync(owner, release.Token);
    }
  }

  private static bool Recent(FuelExchangeRate? rate, DateTime now) =>
    Valid(rate, now) && now - rate!.RetrievedAt < TimeSpan.FromHours(1);

  private static bool Valid(FuelExchangeRate? rate, DateTime now)
  {
    if (
      rate is null
      || rate.UsdPerCad is < .1m or > 2m
      || rate.RetrievedAt.Kind != DateTimeKind.Utc
      || rate.RetrievedAt == default
      || rate.RetrievedAt > now
    )
      return false;
    var today = DateOnly.FromDateTime(now);
    return rate.ObservedOn <= today
      && rate.ObservedOn >= today.AddDays(-7)
      && rate.ObservedOn <= DateOnly.FromDateTime(rate.RetrievedAt);
  }
}
