using Infrastructure.Persistence;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// Audit F27: the webhook that finds no provider id and the sender that
// saves it take the same lock, so whichever comes second sees what the
// first committed. SQLite takes no lock (one writer at a time); on
// PostgreSQL a second holder of the same key waits for the first's
// commit, and another key does not.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class DeliveryStatusLockPostgresTests
{
  [RequiresPostgresFact]
  public async Task TheSameMessageWaitsForTheFirstCommit()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var first = postgres.Connect();
    await using var second = postgres.Connect();
    await using var third = postgres.Connect();
    var company = Guid.NewGuid();

    await using var held = await first.Database.BeginTransactionAsync();
    await new DeliveryStatusLocks(first).LockAsync(
      company,
      "whatsapp",
      "123456",
      "wamid.1",
      default
    );

    await using var other = await third.Database.BeginTransactionAsync();
    await new DeliveryStatusLocks(third)
      .LockAsync(company, "whatsapp", "123456", "wamid.2", default)
      .WaitAsync(TimeSpan.FromSeconds(5));
    await other.CommitAsync();

    await using var waiting = await second.Database.BeginTransactionAsync();
    var same = new DeliveryStatusLocks(second).LockAsync(
      company,
      "whatsapp",
      "123456",
      "wamid.1",
      default
    );
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    Assert.False(same.IsCompleted);

    await held.CommitAsync();
    await same.WaitAsync(TimeSpan.FromSeconds(5));
    await waiting.CommitAsync();
  }
}
