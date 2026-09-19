using Application.Features.Fuel.Models;
using Infrastructure.Integrations.Google.Gmail;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class GmailWatchStoreTests
{
  [Fact]
  public async Task DurableWatchUsesSeparateLeaseAndRejectsStaleOwnerWritesAndRelease()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var store = new GmailWatchStore(db);
    var now = DateTime.UtcNow;
    Assert.Null(await store.ReadAsync(default));
    Assert.True(await store.AcquireAsync("first", now, default));
    Assert.Null(await store.ReadAsync(default));
    Assert.True(
      await new SynchronizationStore(db).AcquireAsync("fleet", now, default)
    );
    Assert.False(await store.AcquireAsync("second", now, default));
    await store.SaveAsync(
      "first",
      new GmailWatchState
      {
        RegisteredAt = now,
        ExpiresAt = now.AddDays(7),
        NextRenewalAt = now.AddDays(1),
        HistoryId = 42,
      },
      default
    );
    await db
      .SynchronizationCheckpoints.Where(x => x.Id == GmailWatchStore.Id)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.LeaseUntil, now.AddSeconds(-1))
      );
    Assert.True(await store.AcquireAsync("second", now, default));
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => store.SaveAsync("first", new(), default)
    );
    await store.ReleaseAsync("first", default);
    Assert.Equal(
      "second",
      await db
        .SynchronizationCheckpoints.Where(x => x.Id == GmailWatchStore.Id)
        .Select(x => x.Owner)
        .SingleAsync()
    );
    await using var restarted = new AppDbContext(options);
    var persisted = await new GmailWatchStore(restarted).ReadAsync(default);
    Assert.NotNull(persisted);
    Assert.Equal(now.AddDays(7), persisted.ExpiresAt);
    Assert.Equal(42UL, persisted.HistoryId);
    Assert.Equal(
      "fleet",
      await db
        .SynchronizationCheckpoints.Where(x => x.Id == SynchronizationStore.Id)
        .Select(x => x.Owner)
        .SingleAsync()
    );
  }

  [Fact]
  public async Task MissingCredentialsFailLocallyWithoutInteractiveAuthorization()
  {
    var factory = new GmailServiceFactory(new StubProviderCredentials());
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => factory.CreateAsync()
    );
  }
}
