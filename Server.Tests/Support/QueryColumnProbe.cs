using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

// What a read actually sent. Columns answer "did this hydrate more than the
// consumer needs"; the statements answer "how many round trips was that", which
// is the question a repeated-query fix has to settle. Transaction boundaries
// are counted apart from statements, because a snapshot read pays for its BEGIN
// and COMMIT as well and they do not reach a command interceptor.
internal sealed class QueryColumnProbe
  : DbCommandInterceptor,
    IDbTransactionInterceptor
{
  public List<string[]> Columns { get; } = [];
  public List<string> Statements { get; } = [];
  public int TransactionsStarted { get; private set; }
  public int TransactionsCommitted { get; private set; }
  public List<IsolationLevel> Isolation { get; } = [];

  public int Matching(string fragment) =>
    Statements.Count(x =>
      x.Contains(fragment, StringComparison.OrdinalIgnoreCase)
    );

  public void Clear()
  {
    Columns.Clear();
    Statements.Clear();
    Isolation.Clear();
    TransactionsStarted = 0;
    TransactionsCommitted = 0;
  }

  public ValueTask<DbTransaction> TransactionStartedAsync(
    DbConnection connection,
    TransactionEndEventData eventData,
    DbTransaction result,
    CancellationToken ct = default
  )
  {
    TransactionsStarted++;
    Isolation.Add(result.IsolationLevel);
    return ValueTask.FromResult(result);
  }

  public Task TransactionCommittedAsync(
    DbTransaction transaction,
    TransactionEndEventData eventData,
    CancellationToken ct = default
  )
  {
    TransactionsCommitted++;
    return Task.CompletedTask;
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
