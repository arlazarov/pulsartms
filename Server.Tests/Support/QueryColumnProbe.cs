using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

internal sealed class QueryColumnProbe : DbCommandInterceptor
{
  public List<string[]> Columns { get; } = [];

  public override ValueTask<DbDataReader> ReaderExecutedAsync(
    DbCommand command,
    CommandExecutedEventData eventData,
    DbDataReader result,
    CancellationToken ct = default
  )
  {
    Columns.Add(
      Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray()
    );
    return ValueTask.FromResult(result);
  }
}
