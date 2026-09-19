namespace Application.Features.Execution.Interfaces;

public interface IExecutionReadScope
{
  // Snapshot isolation must cover membership as well as individual rows.
  // Every read in the callback must use the same scoped IAppDbContext.
  Task<T> ReadAsync<T>(
    Func<CancellationToken, Task<T>> read,
    CancellationToken ct,
    bool requireFreshSnapshot = false
  );
}
