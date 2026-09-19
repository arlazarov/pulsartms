using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

internal sealed class DeadheadRatesFailureProbe : DbCommandInterceptor
{
  public bool FailRatesWrite { get; set; }
  public int RouteWritesBeforeFailure { get; private set; }

  public override ValueTask<
    InterceptionResult<DbDataReader>
  > ReaderExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result,
    CancellationToken cancellationToken = default
  )
  {
    if (FailRatesWrite)
    {
      if (command.CommandText.Contains("UPDATE \"DispatchDeadheads\""))
        RouteWritesBeforeFailure++;
      if (command.CommandText.Contains("UPDATE \"DispatchRates\""))
        throw new InvalidOperationException("Rates write failed.");
    }
    return ValueTask.FromResult(result);
  }
}
