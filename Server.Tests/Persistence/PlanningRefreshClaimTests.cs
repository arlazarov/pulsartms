using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// PlanningRefreshStore has two implementations. The one that runs in
// production claims work with FOR UPDATE SKIP LOCKED and queues it with
// INSERT .. ON CONFLICT DO UPDATE; the other is a fallback for SQLite. Every
// existing test runs on SQLite, so the behaviour was covered and the SQL that
// actually runs was not - a wrong column or a broken CTE would have reached
// production with a green suite.
//
// Skipping rather than waiting is the whole point of the claim. Two planning
// workers must take different work, and a worker must not sit behind another
// one's row; without SKIP LOCKED they serialise and the queue drains at the
// speed of one worker.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class PlanningRefreshClaimTests
{
  [RequiresPostgresFact]
  public async Task QueuedWorkIsClaimedByTheQueryThatRunsInProduction()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new PlanningRefreshStore(db);
    var now = DateTime.UtcNow;
    var scope = Scope();

    var requested = await store.RequestAsync(scope, "inputs-1", now, default);
    Assert.True(requested.Pending);

    var work = await store.ClaimAsync(now, TimeSpan.FromMinutes(5), default);

    Assert.NotNull(work);
    Assert.Equal(scope.DispatchId, work!.Scope.DispatchId);
    Assert.Equal(1, work.Version);
  }

  // The defining property: a row another transaction holds is passed over,
  // not waited for.
  [RequiresPostgresFact]
  public async Task ARowHeldByAnotherWorkerIsSkippedRatherThanWaitedFor()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new PlanningRefreshStore(db);
    var now = DateTime.UtcNow;
    var first = Scope();
    var second = Scope();
    await store.RequestAsync(first, "inputs-1", now, default);
    await store.RequestAsync(second, "inputs-2", now.AddSeconds(1), default);

    await using var holder = f.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    var lockedId = await LockOldestAsync(holder);

    var claim = store.ClaimAsync(
      now.AddSeconds(2),
      TimeSpan.FromMinutes(5),
      default
    );
    var work = await claim.WaitAsync(TimeSpan.FromSeconds(10));

    Assert.NotNull(work);
    Assert.NotEqual(lockedId, work!.Id);
    await held.RollbackAsync();
  }

  // With every row held, the claim comes back empty. It does not block, and
  // it does not steal a leased row.
  [RequiresPostgresFact]
  public async Task AFullyHeldQueueReturnsNothingInsteadOfBlocking()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new PlanningRefreshStore(db);
    var now = DateTime.UtcNow;
    await store.RequestAsync(Scope(), "inputs-1", now, default);

    await using var holder = f.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    await LockOldestAsync(holder);

    var claim = store.ClaimAsync(
      now.AddSeconds(2),
      TimeSpan.FromMinutes(5),
      default
    );

    Assert.Null(await claim.WaitAsync(TimeSpan.FromSeconds(10)));
    await held.RollbackAsync();
  }

  // Queuing the same scope again while it is still pending must not create a
  // second row, and must not reset what is already owed.
  [RequiresPostgresFact]
  public async Task RepeatedDemandForOneScopeStaysOneRow()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new PlanningRefreshStore(db);
    var now = DateTime.UtcNow;
    var scope = Scope();

    await store.RequestAsync(scope, "inputs-1", now, default);
    await store.RequestAsync(scope, "inputs-1", now.AddSeconds(1), default);
    var changed = await store.RequestAsync(
      scope,
      "inputs-2",
      now.AddSeconds(2),
      default
    );

    Assert.True(changed.Pending);
    Assert.Equal(1, await db.PlanningRefreshRequests.CountAsync());
    var row = await db.PlanningRefreshRequests.AsNoTracking().SingleAsync();
    Assert.Equal("inputs-2", row.InputSignature);
    Assert.Equal(2, row.RequestedVersion);
  }

  private static PlanningScope Scope() => new(Guid.NewGuid(), null, 0);

  // Takes the row the store's own ordering would take next, using the same
  // predicate, so the claim under test really is being made to step over it.
  private static async Task<string> LockOldestAsync(AppDbContext db)
  {
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    await using var command = connection.CreateCommand();
    command.Transaction = (NpgsqlTransaction)
      db.Database.CurrentTransaction!.GetDbTransaction();
    command.CommandText = """
      SELECT "Id" FROM "PlanningRefreshRequests"
      WHERE "CompletedVersion" < "RequestedVersion"
      ORDER BY "AvailableAt", "Id"
      LIMIT 1 FOR UPDATE
      """;
    return (string)(await command.ExecuteScalarAsync())!;
  }
}
