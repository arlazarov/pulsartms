namespace Infrastructure.Integrations.TomTom;

internal sealed class TomTomRequestGates
{
  // Entries belong only to active callers/waiters, so completed request hashes
  // are not retained.
  private readonly object sync = new();
  private readonly Dictionary<string, Entry> entries = new(
    StringComparer.Ordinal
  );

  private sealed class Entry
  {
    public readonly SemaphoreSlim Gate = new(1, 1);
    public int Users;
  }

  public async Task<IDisposable> EnterAsync(string key, CancellationToken ct)
  {
    Entry entry;
    lock (sync)
    {
      if (!entries.TryGetValue(key, out entry!))
        entries.Add(key, entry = new());
      entry.Users++;
    }
    try
    {
      await entry.Gate.WaitAsync(ct);
    }
    catch
    {
      Release(key, entry, false);
      throw;
    }
    return new Lease(this, key, entry);
  }

  private void Release(string key, Entry entry, bool acquired)
  {
    lock (sync)
    {
      if (acquired)
        entry.Gate.Release();
      if (--entry.Users != 0)
        return;
      entries.Remove(key);
      entry.Gate.Dispose();
    }
  }

  private sealed class Lease(TomTomRequestGates owner, string key, Entry entry)
    : IDisposable
  {
    private int disposed;

    public void Dispose()
    {
      if (Interlocked.Exchange(ref disposed, 1) == 0)
        owner.Release(key, entry, true);
    }
  }
}
