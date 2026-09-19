using Server.Tests.Support;

namespace Server.Tests.Persistence;

// LockRouteBudgetAsync, LockFuelImportAsync and LockDispatchRatesAsync are
// written as:
//
//   Database.IsNpgsql() ? ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(..)")
//                       : Task.CompletedTask
//
// Every other test in this suite runs on SQLite, so until these existed the
// second branch was the only one ever taken and no test had ever executed the
// lock at all. They guard the routing API budget, the fuel discount import
// and dispatch rates; two of the three decide money.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class AdvisoryLockTests
{
  [RequiresPostgresTheory]
  [InlineData("route-budget")]
  [InlineData("fuel-import")]
  [InlineData("dispatch-rates")]
  public async Task AHeldLockMakesTheSecondHolderWait(string lockName)
  {
    await using var f = await PostgresFixture.CreateAsync();

    await using var first = f.Connect();
    await using var firstTransaction =
      await first.Database.BeginTransactionAsync();
    await Take(first, lockName);

    await using var second = f.Connect();
    await using var secondTransaction =
      await second.Database.BeginTransactionAsync();
    var waiting = Take(second, lockName);

    // Not "has not finished yet" by luck: it is still waiting after long
    // enough that an unlocked statement would have returned many times over.
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    Assert.False(
      waiting.IsCompleted,
      $"The {lockName} lock let a second holder through while it was held."
    );

    await firstTransaction.CommitAsync();
    await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.True(waiting.IsCompletedSuccessfully);
    await secondTransaction.CommitAsync();
  }

  // The lock lasts as long as the transaction that took it, so a caller that
  // rolls back must not keep the next one out.
  [RequiresPostgresTheory]
  [InlineData("route-budget")]
  [InlineData("fuel-import")]
  [InlineData("dispatch-rates")]
  public async Task ARolledBackHolderReleasesTheLock(string lockName)
  {
    await using var f = await PostgresFixture.CreateAsync();

    await using var first = f.Connect();
    await using (var transaction = await first.Database.BeginTransactionAsync())
    {
      await Take(first, lockName);
      await transaction.RollbackAsync();
    }

    await using var second = f.Connect();
    await using var secondTransaction =
      await second.Database.BeginTransactionAsync();

    await Take(second, lockName).WaitAsync(TimeSpan.FromSeconds(10));
    await secondTransaction.CommitAsync();
  }

  // Different work must not queue behind unrelated work: the three ids are
  // distinct, and a copied id would serialise fuel imports behind rate
  // changes without anything saying so.
  [RequiresPostgresFact]
  public async Task TheThreeLocksDoNotBlockEachOther()
  {
    await using var f = await PostgresFixture.CreateAsync();

    await using var first = f.Connect();
    await using var firstTransaction =
      await first.Database.BeginTransactionAsync();
    await Take(first, "route-budget");

    await using var second = f.Connect();
    await using var secondTransaction =
      await second.Database.BeginTransactionAsync();

    await Take(second, "fuel-import").WaitAsync(TimeSpan.FromSeconds(10));
    await Take(second, "dispatch-rates").WaitAsync(TimeSpan.FromSeconds(10));

    await secondTransaction.CommitAsync();
    await firstTransaction.CommitAsync();
  }

  private static Task Take(
    Infrastructure.Persistence.AppDbContext db,
    string lockName
  ) =>
    lockName switch
    {
      "route-budget" => db.LockRouteBudgetAsync(default),
      "fuel-import" => db.LockFuelImportAsync(default),
      "dispatch-rates" => db.LockDispatchRatesAsync(default),
      _ => throw new ArgumentOutOfRangeException(nameof(lockName)),
    };
}
