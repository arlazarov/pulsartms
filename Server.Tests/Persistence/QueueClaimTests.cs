using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// The other two queues that claim with FOR UPDATE SKIP LOCKED. Like the
// planning refresh queue, each has a SQLite fallback that every other test
// takes, so the statement that runs in production had never run. Two workers
// must take different work and neither may wait behind the other; without
// skipping they serialise and the queue drains at the speed of one.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class QueueClaimTests
{
  [RequiresPostgresFact]
  public async Task ExecutionPlanningClaimsWithTheProductionQuery()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var now = DateTime.UtcNow;
    var queued = await QueueChangeAsync(db, now);

    var work = await new ExecutionPlanningStore(db).ClaimAsync(now, default);

    Assert.NotNull(work);
    Assert.Equal(queued, work!.Id);
    Assert.Equal(1, work.Attempts);
    Assert.NotNull(work.LeaseId);
  }

  [RequiresPostgresFact]
  public async Task ExecutionPlanningSkipsAChangeAnotherWorkerHolds()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var now = DateTime.UtcNow;
    await QueueChangeAsync(db, now);
    await QueueChangeAsync(db, now.AddSeconds(1));

    await using var holder = f.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    var locked = await LockAsync<Guid>(
      holder,
      """
      SELECT "Id" FROM "ExecutionPlanningChanges"
      WHERE "CompletedAt" IS NULL
      ORDER BY "AvailableAt", "Id" LIMIT 1 FOR UPDATE
      """
    );

    var work = await new ExecutionPlanningStore(db)
      .ClaimAsync(now.AddSeconds(2), default)
      .WaitAsync(TimeSpan.FromSeconds(10));

    Assert.NotNull(work);
    Assert.NotEqual(locked, work!.Id);
    await held.RollbackAsync();
  }

  [RequiresPostgresFact]
  public async Task SourceRoadsClaimWithTheProductionQuery()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new SourceRoadStore(db);
    var now = DateTime.UtcNow;
    var dispatchId = Guid.NewGuid();
    await store.DemandAsync(dispatchId, "identity-1", 1, now, default);

    var work = await store.ClaimAsync(now, TimeSpan.FromMinutes(5), default);

    Assert.NotNull(work);
    Assert.Equal(dispatchId, work!.DispatchId);
  }

  [RequiresPostgresFact]
  public async Task SourceRoadsSkipARequestAnotherWorkerHolds()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new SourceRoadStore(db);
    var now = DateTime.UtcNow;
    await store.DemandAsync(Guid.NewGuid(), "identity-1", 1, now, default);
    await store.DemandAsync(
      Guid.NewGuid(),
      "identity-2",
      1,
      now.AddSeconds(1),
      default
    );

    await using var holder = f.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    var locked = await LockAsync<Guid>(
      holder,
      """
      SELECT "DispatchId" FROM "SourceRoadRequests"
      WHERE "CompletedVersion" < "RequestedVersion"
      ORDER BY "Priority", "AvailableAt", "DispatchId" LIMIT 1 FOR UPDATE
      """
    );

    var work = await store
      .ClaimAsync(now.AddSeconds(2), TimeSpan.FromMinutes(5), default)
      .WaitAsync(TimeSpan.FromSeconds(10));

    Assert.NotNull(work);
    Assert.NotEqual(locked, work!.DispatchId);
    await held.RollbackAsync();
  }

  // Priority comes before age in the ordering, and that ordering is written
  // only in the statement this test exercises.
  [RequiresPostgresFact]
  public async Task SourceRoadsTakeThePriorityRequestBeforeTheOlderOne()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var store = new SourceRoadStore(db);
    var now = DateTime.UtcNow;
    await store.DemandAsync(Guid.NewGuid(), "older", 5, now, default);
    var urgent = Guid.NewGuid();
    await store.DemandAsync(urgent, "urgent", 1, now.AddSeconds(30), default);

    var work = await store.ClaimAsync(
      now.AddMinutes(1),
      TimeSpan.FromMinutes(5),
      default
    );

    Assert.Equal(urgent, work!.DispatchId);
  }

  private static async Task<Guid> QueueChangeAsync(
    AppDbContext db,
    DateTime availableAt
  )
  {
    var change = new ExecutionPlanningChange
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 1,
      RequestedAt = availableAt,
      AvailableAt = availableAt,
    };
    db.ExecutionPlanningChanges.Add(change);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    return change.Id;
  }

  // Holds the row the store's own ordering would take next, using the same
  // predicate, so the claim really is being made to step over it.
  private static async Task<T> LockAsync<T>(AppDbContext db, string sql)
  {
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    await using var command = connection.CreateCommand();
    command.Transaction = (NpgsqlTransaction)
      db.Database.CurrentTransaction!.GetDbTransaction();
    command.CommandText = sql;
    return (T)(await command.ExecuteScalarAsync())!;
  }
}
