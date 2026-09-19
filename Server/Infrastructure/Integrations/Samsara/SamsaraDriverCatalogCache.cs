using System.Collections.Frozen;
using System.Text.Json;
using Infrastructure.Integrations.Samsara.Models;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraDriverCatalogCache(TimeProvider clock) : IDisposable
{
  public sealed record HosSettings(
    string Timezone,
    int EldDayStartHour,
    string? UsCycle,
    string? CanadaCycle
  );

  private sealed record Snapshot(
    byte[] Json,
    FrozenDictionary<string, HosSettings> Settings,
    DateTimeOffset Fetched
  );

  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );
  private readonly SemaphoreSlim gate = new(1, 1);
  private Snapshot? saved;
  private DateTimeOffset? retryAfter;

  public async Task<IReadOnlyList<SamsaraDriver>> GetAsync(
    SamsaraApiService api,
    CancellationToken ct,
    bool forceRefresh = false
  ) =>
    JsonSerializer.Deserialize<SamsaraDriver[]>(
      (await ReadAsync(api, ct, forceRefresh)).Json,
      Json
    )!;

  public async Task<HosSettings?> GetSettingsAsync(
    string driverId,
    SamsaraApiService api,
    CancellationToken ct
  ) => (await ReadAsync(api, ct, false)).Settings.GetValueOrDefault(driverId);

  private bool Current(Snapshot? value, DateTimeOffset now) =>
    value is not null
    && now >= value.Fetched
    && now - value.Fetched < TimeSpan.FromMinutes(15);

  private async Task<Snapshot> ReadAsync(
    SamsaraApiService api,
    CancellationToken ct,
    bool forceRefresh
  )
  {
    ct.ThrowIfCancellationRequested();
    var ready = Volatile.Read(ref saved);
    if (!forceRefresh && Current(ready, clock.GetUtcNow()))
      return ready!;
    await gate.WaitAsync(ct);
    try
    {
      var now = clock.GetUtcNow();
      var current = Current(saved, now);
      if (!forceRefresh && current)
        return saved!;
      if (retryAfter > now)
        throw new HttpRequestException(
          "Samsara driver settings are temporarily unavailable; retry delayed."
        );
      if (!current)
        Volatile.Write(ref saved, null);
      try
      {
        var drivers = await api.GetDriversAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(drivers, Json);
        var settings = drivers
          .DistinctBy(x => x.Id)
          .ToFrozenDictionary(
            x => x.Id,
            x => new HosSettings(
              x.Timezone,
              x.EldDayStartHour,
              x.EldSettings?.Rulesets.FirstOrDefault(r =>
                r.Shift == "US Interstate Property"
              )?.Cycle,
              x.EldSettings?.Rulesets.FirstOrDefault(r =>
                r.Jurisdiction == "CS"
              )?.Cycle
            ),
            StringComparer.Ordinal
          );
        var size =
          json.LongLength
          + settings.Sum(x =>
            128L
            + 2L
              * (
                x.Key.Length
                + x.Value.Timezone.Length
                + (x.Value.UsCycle?.Length ?? 0)
                + (x.Value.CanadaCycle?.Length ?? 0)
              )
          );
        var snapshot = new Snapshot(json, settings, now);
        ct.ThrowIfCancellationRequested();
        Volatile.Write(ref saved, size <= 4 * 1024 * 1024 ? snapshot : null);
        retryAfter = null;
        return snapshot;
      }
      catch (Exception ex)
        when (ex
            is HttpRequestException
              or JsonException
              or InvalidOperationException
          || ex is OperationCanceledException && !ct.IsCancellationRequested
        )
      {
        retryAfter = now.AddMinutes(1);
        throw;
      }
    }
    finally
    {
      gate.Release();
    }
  }

  public void Dispose() => gate.Dispose();
}
