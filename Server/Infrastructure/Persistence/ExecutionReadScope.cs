using System.Data;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Execution.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence;

public sealed class ExecutionReadScope(AppDbContext db) : IExecutionReadScope
{
  public async Task<T> ReadAsync<T>(
    Func<CancellationToken, Task<T>> read,
    CancellationToken ct,
    bool requireFreshSnapshot = false
  )
  {
    ct.ThrowIfCancellationRequested();
    var level = db.Database.ProviderName switch
    {
      "Npgsql.EntityFrameworkCore.PostgreSQL" => IsolationLevel.RepeatableRead,
      "Microsoft.EntityFrameworkCore.Sqlite" => IsolationLevel.Serializable,
      _ => throw new NotSupportedException(
        "The database provider cannot supply an execution read snapshot."
      ),
    };
    if (db.Database.CurrentTransaction is { } outer)
    {
      if (requireFreshSnapshot)
        throw new InvalidOperationException(
          "Execution revalidation requires a fresh database snapshot."
        );
      var isolation = outer.GetDbTransaction().IsolationLevel;
      if (
        isolation
        is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable)
      )
        throw new InvalidOperationException(
          "Execution reads require repeatable-read or serializable isolation."
        );
      return await read(ct);
    }
    return await db
      .Database.CreateExecutionStrategy()
      .ExecuteAsync(async () =>
      {
        // "open" covers getting a connection and beginning the transaction
        // together. It does not separate a pool wait from the BEGIN itself,
        // and must not be read as either one alone. Nothing is recorded on
        // the path that joins an outer transaction, so a count here is a
        // snapshot this scope actually opened.
        var opening = Stopwatch.GetTimestamp();
        await using var transaction = await db.Database.BeginTransactionAsync(
          level,
          ct
        );
        PerformanceStages.Elapsed("execution-scope", "open", opening);
        var result = await read(ct);
        var committing = Stopwatch.GetTimestamp();
        await transaction.CommitAsync(ct);
        PerformanceStages.Elapsed("execution-scope", "commit", committing);
        return result;
      });
  }
}
