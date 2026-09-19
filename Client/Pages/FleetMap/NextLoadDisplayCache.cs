namespace Client.Pages.FleetMap;

internal sealed class NextLoadDisplayCache(
  int maxEntries = 12,
  int maxBytes = 8 * 1024 * 1024
)
{
  private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
  private readonly Dictionary<
    (Guid Truck, Guid Dispatch, Guid? ExecutionLeg, long AssignmentRevision),
    Snapshot
  > _entries = [];
  private long _sequence;
  private int _bytes;

  internal sealed record Snapshot(
    string Revision,
    byte[] Payload,
    DateTimeOffset Expires,
    long Used
  );

  public Snapshot? Get(
    (Guid Truck, Guid Dispatch) identity,
    DateTimeOffset now,
    Guid? executionLegId = null,
    long assignmentRevision = 0
  )
  {
    Prune(now);
    var key = (
      identity.Truck,
      identity.Dispatch,
      executionLegId,
      assignmentRevision
    );
    if (!_entries.TryGetValue(key, out var snapshot))
      return null;
    _entries[key] = snapshot with { Used = ++_sequence };
    return snapshot;
  }

  public void Store(
    (Guid Truck, Guid Dispatch) identity,
    string revision,
    byte[] payload,
    DateTimeOffset now,
    Guid? executionLegId = null,
    long assignmentRevision = 0
  )
  {
    Prune(now);
    var key = (
      identity.Truck,
      identity.Dispatch,
      executionLegId,
      assignmentRevision
    );
    Remove(key);
    if (maxEntries <= 0 || payload.Length > maxBytes)
      return;
    while (_entries.Count >= maxEntries || _bytes > maxBytes - payload.Length)
      Remove(_entries.MinBy(x => x.Value.Used).Key);
    _entries[key] = new(revision, payload, now + Lifetime, ++_sequence);
    _bytes += payload.Length;
  }

  public void Clear()
  {
    _entries.Clear();
    _bytes = 0;
  }

  private void Prune(DateTimeOffset now)
  {
    foreach (
      var key in _entries
        .Where(x => x.Value.Expires <= now)
        .Select(x => x.Key)
        .ToArray()
    )
      Remove(key);
  }

  private void Remove(
    (
      Guid Truck,
      Guid Dispatch,
      Guid? ExecutionLeg,
      long AssignmentRevision
    ) identity
  )
  {
    if (_entries.Remove(identity, out var snapshot))
      _bytes -= snapshot.Payload.Length;
  }
}
