using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

internal sealed class HistoricalReadProbe : DbCommandInterceptor
{
  public bool Enabled { get; set; }
  public Action? BeforeFirstRead { get; set; }
  public List<DbTransaction?> Transactions { get; } = [];
  public List<string> Commands { get; } = [];

  public override ValueTask<
    InterceptionResult<DbDataReader>
  > ReaderExecutingAsync(
    DbCommand command,
    CommandEventData eventData,
    InterceptionResult<DbDataReader> result,
    CancellationToken cancellationToken = default
  )
  {
    if (Enabled)
    {
      if (Transactions.Count == 0)
        BeforeFirstRead?.Invoke();
      Transactions.Add(command.Transaction);
      Commands.Add(command.CommandText);
    }
    return ValueTask.FromResult(result);
  }
}
