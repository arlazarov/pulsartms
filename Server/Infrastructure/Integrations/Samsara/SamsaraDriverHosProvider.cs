using System.Text.Json;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Integrations.Samsara;

public class SamsaraDriverHosProvider(
  SamsaraApiService api,
  IMemoryCache cache,
  ILogger<SamsaraDriverHosProvider> logger
) : IDriverHosRefreshProvider
{
  private static readonly SemaphoreSlim Gate = new(1, 1);
  private const string CacheKey = "samsara:hos-clocks";

  public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  ) => ReadAsync(false, ct);

  public Task<IReadOnlyDictionary<string, DriverHosClocks>> RefreshClocksAsync(
    CancellationToken ct
  ) => ReadAsync(true, ct);

  private async Task<IReadOnlyDictionary<string, DriverHosClocks>> ReadAsync(
    bool refresh,
    CancellationToken ct
  )
  {
    IReadOnlyDictionary<string, DriverHosClocks>? saved;
    if (!refresh && cache.TryGetValue(CacheKey, out saved))
      return saved!;
    await Gate.WaitAsync(ct);
    try
    {
      if (!refresh && cache.TryGetValue(CacheKey, out saved))
        return saved!;
      IReadOnlyDictionary<string, DriverHosClocks> result;
      try
      {
        var rows = await api.GetHosClocksAsync(ct);
        var now = DateTime.UtcNow;
        result = rows.Where(x =>
            x.Clocks is not null && !string.IsNullOrWhiteSpace(x.Driver.Id)
          )
          .DistinctBy(x => x.Driver.Id)
          .ToDictionary(
            x => x.Driver.Id,
            x => new DriverHosClocks
            {
              BreakMs = x.Clocks!.Break?.TimeUntilBreakDurationMs,
              DriveMs = x.Clocks.Drive?.DriveRemainingDurationMs,
              ShiftMs = x.Clocks.Shift?.ShiftRemainingDurationMs,
              CycleMs = x.Clocks.Cycle?.CycleRemainingDurationMs,
              UpdatedAt = now,
              CurrentDutyStatus = x.CurrentDutyStatus is null
                ? null
                : SamsaraHosHistoryProvider.NormalizeStatus(
                  x.CurrentDutyStatus.HosStatusType
                ),
            }
          );
      }
      catch (Exception ex)
        when (!refresh
          && (
            ex
              is HttpRequestException
                or JsonException
                or InvalidOperationException
            || ex is OperationCanceledException && !ct.IsCancellationRequested
          )
        )
      {
        logger.LogWarning(
          ex,
          "Samsara HOS clocks unavailable; retry delayed for one minute"
        );
        result = new Dictionary<string, DriverHosClocks>();
      }
      cache.Set(CacheKey, result, TimeSpan.FromMinutes(1));
      return result;
    }
    finally
    {
      Gate.Release();
    }
  }
}
