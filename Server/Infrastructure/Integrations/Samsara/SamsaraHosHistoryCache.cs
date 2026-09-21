using Application.Interfaces;
using Domain.Models.Eta;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraHosHistoryCache(
  TimeProvider clock,
  ICurrentCompany companies
) : IDisposable
{
  public sealed record Snapshot(
    HosHistory? Baseline,
    DateTimeOffset Fetched,
    DateTimeOffset FullFetch,
    bool Available
  );

  private sealed record Entry(Snapshot Value, DateTimeOffset Expires);

  private const long MaximumBytes = 16 * 1024 * 1024;
  private readonly MemoryCache entries = new(
    new MemoryCacheOptions { SizeLimit = MaximumBytes }
  );
  private readonly SemaphoreSlim[] gates = Enumerable
    .Range(0, 64)
    .Select(_ => new SemaphoreSlim(1, 1))
    .ToArray();

  public SemaphoreSlim Gate(string driverId) =>
    gates[(uint)StringComparer.Ordinal.GetHashCode(driverId) % gates.Length];

  public Snapshot? Get(string driverId)
  {
    driverId = Key(driverId);
    if (!entries.TryGetValue<Entry>(driverId, out var entry))
      return null;
    if (entry!.Expires > clock.GetUtcNow())
      return entry.Value;
    entries.Remove(driverId);
    return null;
  }

  public Snapshot Store(string driverId, Snapshot snapshot)
  {
    driverId = Key(driverId);
    if (snapshot.Baseline is { } baseline)
      snapshot = snapshot with
      {
        Baseline = baseline with
        {
          Periods = Array.AsReadOnly(baseline.Periods.ToArray()),
        },
      };
    var size =
      1024L
      + driverId.Length * 2L
      + (snapshot.Baseline?.Periods.Sum(x => 64L + x.Status.Length * 2L) ?? 0);
    if (size > MaximumBytes)
    {
      entries.Remove(driverId);
      return snapshot;
    }
    entries.Set(
      driverId,
      new Entry(snapshot, clock.GetUtcNow().AddMinutes(30)),
      new MemoryCacheEntryOptions
      {
        Size = size,
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
      }
    );
    return snapshot;
  }

  private string Key(string driverId) =>
    $"{companies.Id ?? throw new InvalidOperationException("HOS history requires a company."):N}:{driverId}";

  public void Dispose()
  {
    entries.Dispose();
    foreach (var gate in gates)
      gate.Dispose();
  }
}
