using Domain.Policies;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Background;

public sealed class RoutePreparationQueue(
  IOptions<RoutePreparationOptions> options,
  TimeProvider time
)
{
  public sealed record Work(
    Guid DispatchId,
    long Version,
    bool Explicit,
    string? Fingerprint,
    long ConnectionVersion
  )
  {
    public int Priority { get; init; }
  }

  private sealed class Entry(Guid id, DateTimeOffset now)
  {
    public Guid Id { get; } = id;
    public Guid? TruckId;
    public HashSet<Guid> TruckIds = [];
    public string? Fingerprint;
    public string? DemandIdentity;
    public long Version;
    public long ConnectionVersion;
    public bool Pending;
    public bool Running;
    public bool Explicit;
    public bool AssignedDemand;
    public int Priority;
    public int Attempts;
    public DateTimeOffset Seen = now;
    public DateTimeOffset Queued = now;
    public DateTimeOffset Due = now;
    public DateTimeOffset? Completed;
    public long CompletedVersion = -1;
  }

  private readonly object gate = new();
  private readonly Dictionary<Guid, Entry> entries = [];
  public int Capacity => options.Value.PendingCapacity;
  public int PendingCount
  {
    get
    {
      lock (gate)
        return entries.Values.Count(x => x.Pending);
    }
  }
  public int StateCount
  {
    get
    {
      lock (gate)
        return entries.Count;
    }
  }

  public void Request(Guid dispatchId, string identity, int priority = 0)
  {
    lock (gate)
    {
      var entry = Get(dispatchId);
      if (entry is null)
        return;
      if (entry.DemandIdentity == identity)
      {
        if (entry.Pending)
          entry.Priority = Math.Min(entry.Priority, priority);
        else if (!entry.Running && entry.CompletedVersion != entry.Version)
          Enqueue(entry, priority);
        return;
      }
      entry.DemandIdentity = identity;
      entry.Explicit = true;
      Dirty(entry, priority);
    }
  }

  public void Request(Guid dispatchId, Guid truckId, int priority = 1)
  {
    lock (gate)
    {
      var entry = Get(dispatchId);
      if (entry is null)
        return;
      var changed = entry.TruckId is { } known && known != truckId;
      entry.TruckId = truckId;
      entry.TruckIds.Add(truckId);
      entry.AssignedDemand = true;
      // Assigned work may lie beyond the repair scan's date horizon.
      // Repeated demand must retain provider retry deadlines.
      entry.Explicit = true;
      if (changed)
        Dirty(entry, priority);
      else if (entry.Pending)
        entry.Priority = Math.Min(entry.Priority, priority);
      else if (
        !entry.Running
        && (
          entry.CompletedVersion != entry.Version
          || entry.Due <= time.GetUtcNow()
        )
      )
        Enqueue(entry, priority);
    }
  }

  public void MarkDirty(Guid dispatchId, int priority = 1)
  {
    lock (gate)
      if (Get(dispatchId) is { } entry)
        Dirty(entry, priority);
  }

  public void MarkTruckDirty(Guid truckId)
  {
    lock (gate)
      foreach (
        var entry in entries
          .Values.Where(x => x.TruckIds.Contains(truckId))
          .ToArray()
      )
      {
        entry.ConnectionVersion++;
        Dirty(entry, 1);
      }
  }

  public void AddressesChanged(
    IReadOnlyCollection<Guid> dispatchIds,
    IReadOnlyCollection<Guid> truckIds
  )
  {
    lock (gate)
    {
      foreach (
        var entry in entries
          .Values.Where(x =>
            x.TruckIds.Overlaps(truckIds) && !dispatchIds.Contains(x.Id)
          )
          .ToArray()
      )
      {
        entry.ConnectionVersion++;
        Dirty(entry, 1);
      }
      foreach (var id in dispatchIds)
        if (Get(id) is { Running: false } entry)
          Dirty(entry, 1);
    }
  }

  public (Guid? TruckId, long ConnectionVersion) Identity(Guid id)
  {
    lock (gate)
      return entries.TryGetValue(id, out var entry)
        ? (entry.TruckId, entry.ConnectionVersion)
        : (null, 0);
  }

  public void Observe(
    Guid id,
    Guid? truckId,
    string fingerprint,
    int priority,
    IReadOnlyCollection<Guid>? truckIds = null
  )
  {
    lock (gate)
    {
      var entry = Get(id);
      if (entry is null)
        return;
      entry.TruckId = truckId;
      entry.TruckIds =
        truckIds?.ToHashSet() ?? (truckId is { } assigned ? [assigned] : []);
      if (entry.Fingerprint != fingerprint)
      {
        entry.Fingerprint = fingerprint;
        Dirty(entry, priority);
      }
      else if (
        !entry.Pending
        && !entry.Running
        && (entry.Completed is null || entry.Due <= time.GetUtcNow())
      )
        Enqueue(entry, priority);
    }
  }

  public void SetTrucks(Guid id, IEnumerable<Guid> truckIds)
  {
    lock (gate)
      if (Get(id) is { } entry)
        entry.TruckIds = truckIds.ToHashSet();
  }

  public IReadOnlyList<Work> Take(int count)
  {
    lock (gate)
    {
      var due = entries
        .Values.Where(x => x.Pending && !x.Running && x.Due <= time.GetUtcNow())
        .OrderBy(x => x.Priority)
        .ThenBy(x => x.Queued)
        .Take(count)
        .ToArray();
      foreach (var entry in due)
      {
        entry.Pending = false;
        entry.Running = true;
      }
      return due.Select(x => new Work(
          x.Id,
          x.Version,
          x.Explicit,
          x.Fingerprint,
          x.ConnectionVersion
        )
        {
          Priority = x.Priority,
        })
        .ToArray();
    }
  }

  public void Complete(Work work, string fingerprint, Guid? truckId)
  {
    lock (gate)
    {
      if (!entries.TryGetValue(work.DispatchId, out var entry))
        return;
      entry.Running = false;
      if (entry.Version != work.Version)
      {
        Enqueue(entry, entry.Priority);
        return;
      }
      entry.Fingerprint = fingerprint;
      entry.TruckId = truckId;
      if (truckId is { } assigned)
        entry.TruckIds = [assigned];
      entry.Completed = time.GetUtcNow();
      entry.CompletedVersion = entry.Version;
      entry.Due = time.GetUtcNow().AddMinutes(options.Value.RepairMinutes);
      entry.Attempts = 0;
      entry.Explicit = entry.AssignedDemand;
    }
  }

  public void Retry(
    Work work,
    string? fingerprint,
    Guid? truckId,
    DateTime? retryAfter = null
  )
  {
    lock (gate)
    {
      if (!entries.TryGetValue(work.DispatchId, out var entry))
        return;
      entry.Running = false;
      if (entry.Version != work.Version)
      {
        Enqueue(entry, entry.Priority);
        return;
      }
      entry.Fingerprint = fingerprint;
      entry.TruckId = truckId;
      if (truckId is { } assigned)
        entry.TruckIds = [assigned];
      var now = time.GetUtcNow();
      entry.CompletedVersion = -1;
      var seconds = Math.Min(
        3600,
        options.Value.RetrySeconds * Math.Pow(2, Math.Min(entry.Attempts++, 6))
      );
      entry.Due =
        retryAfter is { } requested
        && requested != DateTime.MaxValue
        && requested > now.UtcDateTime
          ? new DateTimeOffset(
            DateTime.SpecifyKind(requested, DateTimeKind.Utc)
          )
          : now.AddSeconds(seconds);
      Enqueue(entry, entry.Priority);
    }
  }

  private Entry? Get(Guid id)
  {
    var now = time.GetUtcNow();
    if (entries.TryGetValue(id, out var existing))
    {
      existing.Seen = now;
      return existing;
    }
    if (entries.Count >= options.Value.StateCapacity)
    {
      var oldest = entries
        .Values.Where(x => !x.Running)
        .OrderBy(x => x.Pending)
        .ThenBy(x => x.Seen)
        .FirstOrDefault();
      if (oldest is null)
        return null;
      entries.Remove(oldest.Id);
    }
    var entry = new Entry(id, now);
    entries.Add(id, entry);
    return entry;
  }

  private void Dirty(Entry entry, int priority)
  {
    entry.Version++;
    entry.Due = time.GetUtcNow();
    entry.Attempts = 0;
    Enqueue(entry, priority);
  }

  private void Enqueue(Entry entry, int priority)
  {
    if (entry.Pending)
    {
      entry.Priority = Math.Min(entry.Priority, priority);
      return;
    }
    if (entries.Values.Count(x => x.Pending) >= options.Value.PendingCapacity)
    {
      var last = entries
        .Values.Where(x => x.Pending && !x.Running)
        .OrderByDescending(x => x.Priority)
        .ThenByDescending(x => x.Queued)
        .FirstOrDefault();
      if (last is null || last.Priority <= priority)
        return;
      last.Pending = false;
    }
    entry.Pending = true;
    entry.Priority = priority;
    entry.Queued = time.GetUtcNow();
  }
}
