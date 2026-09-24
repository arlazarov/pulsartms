using Microsoft.Extensions.DependencyInjection;

namespace Application.Diagnostics.Consistency;

// One unit of work: rules, repairs and a journal over one database context.
// The auditor opens one per rule and one for recovery, and disposes it
// whatever happens, so a failed save never leaks its unwritten changes into
// the next operation.
public sealed class ConsistencyWork(
  IReadOnlyList<IConsistencyRule> rules,
  IReadOnlyList<IConsistencyRepair> repairs,
  ConsistencyJournal journal,
  IAsyncDisposable owner
) : IAsyncDisposable
{
  public IReadOnlyList<IConsistencyRule> Rules => rules;
  public IReadOnlyList<IConsistencyRepair> Repairs => repairs;
  public ConsistencyJournal Journal => journal;

  // A scope that cannot yield its services is disposed here; one that can
  // is owned by the work.
  public static ConsistencyWork Open(IServiceScopeFactory scopes)
  {
    var scope = scopes.CreateAsyncScope();
    try
    {
      var services = scope.ServiceProvider;
      return new(
        [.. services.GetServices<IConsistencyRule>()],
        [.. services.GetServices<IConsistencyRepair>()],
        services.GetRequiredService<ConsistencyJournal>(),
        scope
      );
    }
    catch
    {
      scope.Dispose();
      throw;
    }
  }

  public ValueTask DisposeAsync() => owner.DisposeAsync();
}
