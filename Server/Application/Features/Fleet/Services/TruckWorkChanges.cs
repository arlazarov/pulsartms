namespace Application.Features.Fleet.Services;

// The trucks a request's committed change touched. Every command that
// commits a change to current work already names them once it has
// committed - both trucks of a transfer - through
// RoutePreparationQueue.MarkTruckDirty. That call also marks them here, for
// the request it runs in, so their trailers can be resolved as soon as the
// request succeeds. Outside a request nothing is collected.
public static class TruckWorkChanges
{
  private static readonly AsyncLocal<HashSet<Guid>?> Current = new();

  public static IDisposable Collect(out HashSet<Guid> trucks)
  {
    var previous = Current.Value;
    trucks = [];
    Current.Value = trucks;
    return new Restore(previous);
  }

  public static void Mark(Guid truck)
  {
    if (Current.Value is { } trucks)
      lock (trucks)
        trucks.Add(truck);
  }

  // A pass that resolved the whole fleet leaves nothing for this request.
  public static void Handled()
  {
    if (Current.Value is { } trucks)
      lock (trucks)
        trucks.Clear();
  }

  public static Guid[] Take(HashSet<Guid> trucks)
  {
    lock (trucks)
      return [.. trucks];
  }

  private sealed class Restore(HashSet<Guid>? previous) : IDisposable
  {
    public void Dispose() => Current.Value = previous;
  }
}
