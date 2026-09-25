using System.Text.Json;
using Application.Features.Eta.Interfaces;
using Domain.Models.Eta;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraHosHistoryProvider(
  SamsaraApiService api,
  SamsaraHosHistoryCache cache,
  SamsaraDriverCatalogCache catalog,
  TimeProvider clock,
  ILogger<SamsaraHosHistoryProvider> logger
) : IHosHistoryProvider
{
  public async Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(driverId))
      return null;
    var gate = cache.Gate(driverId);
    await gate.WaitAsync(ct);
    try
    {
      var now = clock.GetUtcNow();
      var previous = cache.Get(driverId);
      if (
        previous is not null
        && now >= previous.Fetched
        && now - previous.Fetched < TimeSpan.FromMinutes(1)
      )
        return previous.Available ? previous.Baseline : null;
      try
      {
        var driver = await catalog.GetSettingsAsync(driverId, api, ct);
        if (driver is null || string.IsNullOrWhiteSpace(driver.Timezone))
        {
          cache.Store(driverId, new(null, now, now, false));
          return null;
        }
        _ = TimeZoneInfo.FindSystemTimeZoneById(driver.Timezone);
        var full =
          previous?.Baseline is null
          || now < previous.FullFetch
          || now - previous.FullFetch >= TimeSpan.FromMinutes(15);
        var from = full ? now.AddDays(-16) : now.AddDays(-2);
        var response = await api.GetHosHistoryAsync(driverId, from, now, ct);
        var periods = response
          .Where(x => x.Driver.Id == driverId)
          .SelectMany(x => x.HosLogs)
          .Select(x => new HosPeriod(
            x.LogStartTime < from ? from : x.LogStartTime,
            x.LogEndTime is null || x.LogEndTime > now
              ? now
              : x.LogEndTime.Value,
            NormalizeStatus(x.HosStatusType)
          ))
          .Where(x => x.End > x.Start)
          .ToList();
        if (!full)
          periods.AddRange(
            previous!
              .Baseline!.Periods.Where(x => x.Start < from)
              .Select(x => x with { End = x.End > from ? from : x.End })
          );
        var history = new HosHistory(
          full ? from : previous!.Baseline!.From,
          now,
          driver.Timezone,
          driver.EldDayStartHour,
          ParseRule(driver.UsCycle, "US"),
          ParseRule(driver.CanadaCycle, "CA"),
          periods.Distinct().OrderBy(x => x.Start).ToList()
        );
        ct.ThrowIfCancellationRequested();
        return cache
          .Store(
            driverId,
            new(history, now, full ? now : previous!.FullFetch, true)
          )
          .Baseline;
      }
      catch (Exception ex)
        when (ex
            is HttpRequestException
              or JsonException
              or InvalidOperationException
              or TimeZoneNotFoundException
          || ex is OperationCanceledException && !ct.IsCancellationRequested
        )
      {
        logger.LogWarning(
          ex,
          "Samsara HOS history unavailable for {DriverId}; retry delayed for one minute",
          driverId
        );
        // Keep recovery data without presenting it as fresh HOS during the
        // retry delay.
        cache.Store(
          driverId,
          new(previous?.Baseline, now, previous?.FullFetch ?? now, false)
        );
        return null;
      }
    }
    finally
    {
      gate.Release();
    }
  }

  public static string NormalizeStatus(string status) =>
    status == "sleeperBed" ? "sleeperBerth" : status;

  public static HosCycleRule? ParseRule(string? cycle, string country) =>
    (country, cycle) switch
    {
      ("US", "USA 70 hour / 8 day") => new(8, 70, 34),
      ("US", "USA 60 hour / 7 day") => new(7, 60, 34),
      ("CA", "Canada South Cycle 1 (70 hour / 7 day)") => new(7, 70, 36),
      ("CA", "Canada South Cycle 2 (120 hour / 14 day)") => new(14, 120, 72),
      _ => null,
    };
}
