using System.Collections.Concurrent;

namespace Application.Caching;

// One process-wide gate set per owning service, resolved through DI instead of static fields
// so tests and future multi-instance hosting own their gates explicitly.
public sealed class ProcessGates : IDisposable
{
  private readonly ConcurrentDictionary<Type, KeyedGates> owners = new();
  private readonly ConcurrentDictionary<(Type, int), SemaphoreSlim> semaphores = new();

  public KeyedGates For<TOwner>() => owners.GetOrAdd(typeof(TOwner), _ => new KeyedGates());

  // One process-wide slot for the owner: serialize a whole operation rather than a key.
  public SemaphoreSlim Single<TOwner>() => Slots<TOwner>(1);

  public SemaphoreSlim Slots<TOwner>(int count) =>
    semaphores.GetOrAdd((typeof(TOwner), count), key => new SemaphoreSlim(key.Item2, key.Item2));

  public void Dispose()
  {
    foreach (var gates in owners.Values) gates.Dispose();
    foreach (var semaphore in semaphores.Values) semaphore.Dispose();
    owners.Clear();
    semaphores.Clear();
  }
}
