using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Services;

public sealed class EtaRouteTimingCache(
  int capacity = 128,
  int maximumRetainedUnits = 32768
)
{
  private readonly record struct Key(Guid PlanId, int Version);

  private sealed record Entry(Key Key, EtaRouteTiming Timing);

  private readonly Dictionary<Key, LinkedListNode<Entry>> entries = new();
  private readonly LinkedList<Entry> recent = new();
  private readonly object sync = new();
  private readonly object[] gates = Enumerable
    .Range(0, 16)
    .Select(_ => new object())
    .ToArray();
  private readonly int capacity = Math.Max(1, capacity);
  private readonly int maximumRetainedUnits = Math.Max(1, maximumRetainedUnits);
  private int retainedUnits;

  public int Count
  {
    get
    {
      lock (sync)
        return entries.Count;
    }
  }
  public int RetainedUnits
  {
    get
    {
      lock (sync)
        return retainedUnits;
    }
  }

  public EtaRouteTiming GetOrCreate(
    Guid planId,
    int version,
    TruckRoute route,
    IRouteRegionLookup regions
  )
  {
    var key = new Key(planId, version);
    if (Find(key, route) is { } cached)
      return cached;
    lock (gates[(uint)key.GetHashCode() % gates.Length])
    {
      if (Find(key, route) is { } ready)
        return ready;
      var timing = EtaRouteTiming.Compile(route, regions);
      lock (sync)
      {
        if (entries.TryGetValue(key, out var previous))
          Remove(previous);
        if (timing.RetainedUnits > maximumRetainedUnits)
          return timing;
        while (
          recent.Last is { } oldest
          && (
            entries.Count >= capacity
            || retainedUnits + timing.RetainedUnits > maximumRetainedUnits
          )
        )
          Remove(oldest);
        entries[key] = recent.AddFirst(new Entry(key, timing));
        retainedUnits += timing.RetainedUnits;
      }
      return timing;
    }
  }

  private EtaRouteTiming? Find(Key key, TruckRoute route)
  {
    lock (sync)
    {
      // A plan version owns immutable geometry; cheap leg metadata catches
      // accidental key reuse.
      if (
        !entries.TryGetValue(key, out var node)
        || !node.Value.Timing.Matches(route)
      )
        return null;
      recent.Remove(node);
      recent.AddFirst(node);
      return node.Value.Timing;
    }
  }

  private void Remove(LinkedListNode<Entry> node)
  {
    entries.Remove(node.Value.Key);
    recent.Remove(node);
    retainedUnits -= node.Value.Timing.RetainedUnits;
  }
}
