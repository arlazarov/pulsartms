using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

// What a read actually sent. Columns answer "did this hydrate more than the
// consumer needs"; the statements answer "how many round trips was that", which
// is the question a repeated-query fix has to settle. Transaction boundaries do
// not pass through a command interceptor, so a snapshot read's BEGIN and COMMIT
// are not counted here.
internal sealed class QueryColumnProbe : DbCommandInterceptor
{
  public List<string[]> Columns { get; } = [];
  public List<string> Statements { get; } = [];

  public int Matching(string fragment) =>
    Statements.Count(x =>
      x.Contains(fragment, StringComparison.OrdinalIgnoreCase)
    );

  public void Clear()
  {
    Columns.Clear();
    Statements.Clear();
  }

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
    Statements.Add(command.CommandText);
    return ValueTask.FromResult(result);
  }
}
