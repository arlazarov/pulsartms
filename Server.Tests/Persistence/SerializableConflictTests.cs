using System.Data;
using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// Twenty-six files decide what to do about a lost write by asking
// IsWriteConflict. It answers for DbUpdateConcurrencyException and for three
// Postgres states - serialization failure, deadlock and unique violation -
// and the Postgres half of that cannot happen on SQLite, so no test had ever
// handed it a real one. If it stopped recognising them, every one of those
// callers would turn a conflict into a failed request or, worse, a silent
// one, and the suite would stay green.
//
// Underneath it is an assumption about the engine: that two transactions
// changing the same row cannot both commit. That is what makes two
// dispatchers editing one load safe, and it is worth stating rather than
// believing.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class SerializableConflictTests
{
  [RequiresPostgresFact]
  public async Task TwoWritersOnOneRowCannotBothCommit()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var id = await SeedAsync(f);

    await using var first = f.Connect();
    await using var second = f.Connect();
    await using var firstTransaction =
      await first.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    await using var secondTransaction =
      await second.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    await BumpAsync(first, id);
    // Blocks on the row the first writer holds, and is answered only once
    // that writer commits - at which point this one can no longer be
    // serialised before it.
    var contending = BumpAsync(second, id);
    await Task.Delay(TimeSpan.FromMilliseconds(300));
    Assert.False(contending.IsCompleted);

    await firstTransaction.CommitAsync();
    var conflict = await Assert.ThrowsAnyAsync<Exception>(
      () => contending.WaitAsync(TimeSpan.FromSeconds(10))
    );

    Assert.True(
      second.IsWriteConflict(conflict),
      $"A lost write was not recognised as a conflict: {conflict.GetType()}"
    );
    Assert.Equal(1, await AttemptsAsync(f, id));
  }

  // The other state the callers rely on. A retry that treats it as an
  // ordinary failure gives up on work that would have succeeded.
  [RequiresPostgresFact]
  public async Task ADuplicateKeyIsRecognisedAsAWriteConflict()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var id = await SeedAsync(f);

    await using var db = f.Connect();
    db.ExecutionPlanningChanges.Add(
      new ExecutionPlanningChange
      {
        Id = id,
        DispatchId = Guid.NewGuid(),
        TruckId = Guid.NewGuid(),
        ExecutionLegId = Guid.NewGuid(),
        RequestedAt = DateTime.UtcNow,
        AvailableAt = DateTime.UtcNow,
      }
    );

    var conflict = await Assert.ThrowsAnyAsync<Exception>(
      () => db.SaveChangesAsync()
    );

    Assert.True(
      db.IsWriteConflict(conflict),
      $"A duplicate key was not recognised as a conflict: {conflict.GetType()}"
    );
  }

  // Not every failure is a conflict. A caller that retried on all of them
  // would repeat work that can never succeed.
  [RequiresPostgresFact]
  public async Task AnOrdinaryFailureIsNotRecognisedAsAWriteConflict()
  {
    await using var f = await PostgresFixture.CreateAsync();
    await using var db = f.Connect();

    var failure = await Assert.ThrowsAnyAsync<Exception>(
      () => db.Database.ExecuteSqlRawAsync("SELECT no_such_function()")
    );

    Assert.False(db.IsWriteConflict(failure));
  }

  private static Task BumpAsync(AppDbContext db, Guid id) =>
    db
      .ExecutionPlanningChanges.Where(x => x.Id == id)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.Attempts, x => x.Attempts + 1)
      );

  private static async Task<int> AttemptsAsync(PostgresFixture f, Guid id)
  {
    await using var db = f.Connect();
    return await db
      .ExecutionPlanningChanges.Where(x => x.Id == id)
      .Select(x => x.Attempts)
      .SingleAsync();
  }

  private static async Task<Guid> SeedAsync(PostgresFixture f)
  {
    await using var db = f.Connect();
    var change = new ExecutionPlanningChange
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      RequestedAt = DateTime.UtcNow,
      AvailableAt = DateTime.UtcNow,
    };
    db.ExecutionPlanningChanges.Add(change);
    await db.SaveChangesAsync();
    return change.Id;
  }
}
