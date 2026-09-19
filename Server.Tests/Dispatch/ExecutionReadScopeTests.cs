using System.Data;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionReadScopeTests
{
  [Fact]
  public async Task NestedReadsUseOneTransactionAndReleaseOwnedResources()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var scope = new ExecutionReadScope(f.Db);
    var count = await scope.ReadAsync(
      async ct =>
      {
        var transaction = f.Db.Database.CurrentTransaction;
        Assert.NotNull(transaction);
        Assert.Equal(
          IsolationLevel.Serializable,
          transaction.GetDbTransaction().IsolationLevel
        );
        var first = await f.Db.Dispatches.CountAsync(ct);
        return await scope.ReadAsync(
          async nested =>
          {
            Assert.Same(transaction, f.Db.Database.CurrentTransaction);
            return first + await f.Db.Dispatches.CountAsync(nested);
          },
          ct
        );
      },
      default
    );

    Assert.Equal(2, count);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task ReaderDoesNotCommitOrDisposeACallersTransaction()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await using var outer = await f.Db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable
    );
    var scope = new ExecutionReadScope(f.Db);

    await scope.ReadAsync(ct => f.Db.Dispatches.CountAsync(ct), default);

    Assert.Same(outer, f.Db.Database.CurrentTransaction);
    await outer.RollbackAsync();
  }

  [Fact]
  public async Task WeakerOuterIsolationIsRejectedBeforeReading()
  {
    await using var connection = new SqliteConnection(
      $"Data Source=scope-{Guid.NewGuid()};Mode=Memory;Cache=Shared"
    );
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await using var outer = await db.Database.BeginTransactionAsync(
      IsolationLevel.ReadUncommitted
    );
    Assert.Equal(
      IsolationLevel.ReadUncommitted,
      outer.GetDbTransaction().IsolationLevel
    );
    var scope = new ExecutionReadScope(db);
    var called = false;

    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        scope.ReadAsync(
          ct =>
          {
            called = true;
            return Task.FromResult(0);
          },
          default
        )
    );

    Assert.False(called);
    Assert.Same(outer, db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task FailedReadDisposesItsTransactionAndAllowsAFreshRead()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var scope = new ExecutionReadScope(f.Db);

    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        scope.ReadAsync<int>(
          ct => throw new InvalidOperationException("Fixture read failure."),
          default
        )
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      1,
      await scope.ReadAsync(ct => f.Db.Dispatches.CountAsync(ct), default)
    );
  }

  [Fact]
  public async Task CancellationBeforeReadingDoesNotOpenATransaction()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var called = false;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        new ExecutionReadScope(f.Db).ReadAsync(
          ct =>
          {
            called = true;
            return Task.FromResult(0);
          },
          cancellation.Token
        )
    );

    Assert.False(called);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }
}
