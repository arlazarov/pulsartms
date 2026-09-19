namespace Application.Caching;

// Each owner needs its own stripes; nested operations must not reacquire an
// owner's gate.
public sealed class KeyedGates : IDisposable
{
  private readonly SemaphoreSlim[] stripes = Enumerable
    .Range(0, 64)
    .Select(_ => new SemaphoreSlim(1, 1))
    .ToArray();

  public SemaphoreSlim For(Guid key) =>
    stripes[(uint)key.GetHashCode() % stripes.Length];

  public SemaphoreSlim For(string key) =>
    stripes[(uint)StringComparer.Ordinal.GetHashCode(key) % stripes.Length];

  public void Dispose()
  {
    foreach (var stripe in stripes)
      stripe.Dispose();
  }
}
