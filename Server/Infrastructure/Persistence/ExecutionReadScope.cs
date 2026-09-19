using System.Data;
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
        await using var transaction = await db.Database.BeginTransactionAsync(
          level,
          ct
        );
        var result = await read(ct);
        await transaction.CommitAsync(ct);
        return result;
      });
  }
}
