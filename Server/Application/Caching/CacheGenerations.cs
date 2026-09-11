namespace Application.Caching;

internal sealed class CacheGenerations
{
  private const int Capacity = 4096;
  private sealed record Entry(string Group, long Version);
  private readonly Dictionary<string, LinkedListNode<Entry>> entries = new(StringComparer.Ordinal);
  private readonly LinkedList<Entry> recency = new();
  private readonly object gate = new();
  private long revision;
  private long epoch;

  public long Get(string group)
  {
    lock (gate) return Touch(group).Value.Version;
  }

  public void Invalidate(string group)
  {
    lock (gate)
    {
      var node = Touch(group);
      node.Value = new(group, ++revision);
    }
  }

  private LinkedListNode<Entry> Touch(string group)
  {
    if (entries.TryGetValue(group, out var node))
    {
      recency.Remove(node);
      recency.AddLast(node);
      return node;
    }
    if (entries.Count == Capacity)
    {
      entries.Remove(recency.First!.Value.Group);
      recency.RemoveFirst();
      // A forgotten key must never revive a still-cached snapshot with its previous generation.
      epoch = ++revision;
    }
    node = recency.AddLast(new Entry(group, epoch));
    entries.Add(group, node);
    return node;
  }
}
