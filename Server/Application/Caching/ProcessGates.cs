using System.Collections.Concurrent;

namespace Application.Caching;

// One process-wide stripe set per owning service, resolved through DI instead of static fields
// so tests and future multi-instance hosting own their gates explicitly.
public sealed class ProcessGates : IDisposable
{
  private readonly ConcurrentDictionary<Type, KeyedGates> owners = new();

  public KeyedGates For<TOwner>() => owners.GetOrAdd(typeof(TOwner), _ => new KeyedGates());

  public void Dispose()
  {
    foreach (var gates in owners.Values) gates.Dispose();
    owners.Clear();
  }
}
