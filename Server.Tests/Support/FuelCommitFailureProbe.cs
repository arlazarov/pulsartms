using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

public sealed class FuelCommitFailureProbe : DbCommandInterceptor
{
  public bool FailNextSnapshotWrite { get; set; }
  public int RouteWritesBeforeFailure { get; private set; }
  public Action? SnapshotWritten { get; set; }
  public Func<Task>? SnapshotWrittenAsync { get; set; }

  public override ValueTask<
    InterceptionResult<DbDataReader>
  > ReaderExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result,
    CancellationToken cancellationToken = default
  )
  {
    if (
      FailNextSnapshotWrite
      && command.CommandText.Contains(
        "UPDATE \"DispatchRoutePlans\"",
        StringComparison.Ordinal
      )
    )
      RouteWritesBeforeFailure++;
    return ValueTask.FromResult(result);
  }

  public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<int> result,
    CancellationToken cancellationToken = default
  )
  {
    if (FailNextSnapshotWrite && IsSnapshotWrite(command))
    {
      FailNextSnapshotWrite = false;
      throw new InvalidOperationException(
        "Injected truck fuel snapshot write failure."
      );
    }
    return ValueTask.FromResult(result);
  }

  public override async ValueTask<int> NonQueryExecutedAsync(
    DbCommand command,
    CommandExecutedEventData eventData,
    int result,
    CancellationToken cancellationToken = default
  )
  {
    if (IsSnapshotWrite(command))
    {
      SnapshotWritten?.Invoke();
      if (SnapshotWrittenAsync is { } after)
        await after();
    }
    return result;
  }

  private static bool IsSnapshotWrite(DbCommand command) =>
    command.CommandText.Contains(
      "INSERT INTO \"TruckFuelPlans\"",
      StringComparison.Ordinal
    )
    || command.CommandText.Contains(
      "UPDATE \"TruckFuelPlans\"",
      StringComparison.Ordinal
    );
}
