using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Support;

internal sealed class PublicationCommitFailureProbe : DbTransactionInterceptor
{
  public bool FailNextCommit { get; set; }

  public override ValueTask<InterceptionResult> TransactionCommittingAsync(
    DbTransaction transaction,
    TransactionEventData eventData,
    InterceptionResult result,
    CancellationToken cancellationToken = default
  )
  {
    if (FailNextCommit)
    {
      FailNextCommit = false;
      throw new InvalidOperationException("Publication commit failed.");
    }
    return ValueTask.FromResult(result);
  }
}
