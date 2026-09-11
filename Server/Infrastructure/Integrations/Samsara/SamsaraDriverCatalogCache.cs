using Infrastructure.Integrations.Samsara.Models;
using System.Text.Json;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraDriverCatalogCache(TimeProvider clock) : IDisposable
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly SemaphoreSlim gate = new(1, 1);
  private byte[]? saved;
  private DateTimeOffset fetched;
  private DateTimeOffset? retryAfter;

  public async Task<IReadOnlyList<SamsaraDriver>> GetAsync(SamsaraApiService api, CancellationToken ct, bool forceRefresh = false)
  {
    await gate.WaitAsync(ct);
    try
    {
      var now = clock.GetUtcNow();
      var current = saved is not null && now >= fetched && now - fetched < TimeSpan.FromMinutes(15);
      if (!forceRefresh && current)
        return JsonSerializer.Deserialize<SamsaraDriver[]>(saved, Json)!;
      if (retryAfter > now) throw new HttpRequestException("Samsara driver settings are temporarily unavailable; retry delayed.");
      if (!current) saved = null;
      try
      {
        var drivers = await api.GetDriversAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(drivers, Json);
        ct.ThrowIfCancellationRequested();
        saved = json.Length <= 4 * 1024 * 1024 ? json : null;
        fetched = now;
        retryAfter = null;
        return drivers;
      }
      catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
        || ex is OperationCanceledException && !ct.IsCancellationRequested)
      {
        retryAfter = now.AddMinutes(1);
        throw;
      }
    }
    finally { gate.Release(); }
  }

  public void Dispose() => gate.Dispose();
}
