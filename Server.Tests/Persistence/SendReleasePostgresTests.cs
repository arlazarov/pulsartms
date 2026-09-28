using System.Data.Common;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// Root's review of dacf2361: two operators release the same revision at
// once, on PostgreSQL. Both find no record; the first to insert wins, the
// other meets the unique index and answers with the first record - one
// row, no error, the first releaser named in both answers.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class SendReleasePostgresTests
{
  [RequiresPostgresFact]
  public async Task TwoOperatorsReleasingAtOnceLeaveOneRecord()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    var gate = new InsertGate();
    await using var first = postgres.Connect(gate);
    await using var second = postgres.Connect();
    var hold = TestSendHold.Open();

    var releasing = Handlers(first, hold, "operator-1")
      .Handle(new ReleaseSendsCommand(), default);
    await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var other = await Handlers(second, hold, "operator-2")
      .Handle(new ReleaseSendsCommand(), default);
    gate.Proceed.SetResult();
    var late = await releasing.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.True(other.Success);
    Assert.True(late.Success);
    Assert.Equal("operator-2", other.Response!.ReleasedBy);
    Assert.Equal("operator-2", late.Response!.ReleasedBy);
    await using var read = postgres.Connect();
    Assert.Single(await read.SendReleases.AsNoTracking().ToListAsync());
  }

  private static SendHoldHandlers Handlers(
    AppDbContext db,
    SendHold hold,
    string identity
  ) =>
    new(db, hold, new Caller(identity), new Operators(), TimeProvider.System);

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => identity;
  }

  private sealed class Operators : IDeploymentOperators
  {
    public bool Includes(string? identityUserId) =>
      identityUserId is "operator-1" or "operator-2";
  }

  // Holds the first insert of a release record until the test lets it go,
  // after that operator has already found no record.
  private sealed class InsertGate : DbCommandInterceptor
  {
    public TaskCompletionSource Reached { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Proceed { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task WaitAsync(DbCommand command)
    {
      if (
        command.CommandText.Contains("INSERT INTO \"SendReleases\"")
        && Reached.TrySetResult()
      )
        await Proceed.Task;
    }

    public override async ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      await WaitAsync(command);
      return result;
    }

    public override async ValueTask<
      InterceptionResult<int>
    > NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      await WaitAsync(command);
      return result;
    }
  }
}
