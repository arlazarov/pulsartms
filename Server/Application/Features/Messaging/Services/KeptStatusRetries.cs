namespace Application.Features.Messaging.Services;

// Kept-status messages whose reconciliation failed, in this process: each
// is skipped until RetryAfter has passed and then tried again, so one that
// keeps failing cannot take a round's whole batch. Entries past their time
// are dropped at every look, so the map holds only failures of the last
// RetryAfter - at most a carrier's kept rows (EarlyDeliveryStatuses
// .PerCompany) for each carrier that had any.
public sealed class KeptStatusRetries
{
  public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

  public readonly record struct Key(
    Guid Company,
    string Channel,
    string BusinessNumberId,
    string ProviderMessageId
  );

  private readonly object gate = new();
  private readonly Dictionary<Key, DateTime> waiting = [];

  public int Count
  {
    get
    {
      lock (gate)
        return waiting.Count;
    }
  }

  public IReadOnlySet<Key> Waiting(Guid company, DateTime now)
  {
    lock (gate)
    {
      foreach (var due in waiting.Where(x => x.Value <= now).ToList())
        waiting.Remove(due.Key);
      return waiting.Keys.Where(x => x.Company == company).ToHashSet();
    }
  }

  public void Failed(Key key, DateTime now)
  {
    lock (gate)
      waiting[key] = now + RetryAfter;
  }

  public void Done(Key key)
  {
    lock (gate)
      waiting.Remove(key);
  }
}
