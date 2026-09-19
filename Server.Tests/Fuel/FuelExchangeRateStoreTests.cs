using System.Text.Json;
using Application.Features.Fuel.Models;
using Domain.Entities.Fleet;
using Infrastructure.Integrations.Google.Gmail;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelExchangeRateStoreTests
{
  [Theory]
  [InlineData("{}")]
  [InlineData("null")]
  [InlineData("[]")]
  [InlineData("invalid-json")]
  [InlineData("{\"usdPerCad\":")]
  [InlineData("oversized")]
  public async Task MissingOrMalformedStateDoesNotBreakReadsAndCanBeReplaced(
    string payload
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var now = DateTime.UtcNow;
    var rate = new FuelExchangeRate(
      1m / 1.3917m,
      DateOnly.FromDateTime(now).AddDays(-1),
      now
    );
    db.SynchronizationCheckpoints.Add(
      new SynchronizationCheckpoint
      {
        Id = FuelExchangeRateStore.Id,
        StateJson =
          payload == "oversized"
            ? new string(' ', 4096) + JsonSerializer.Serialize(rate)
            : payload,
      }
    );
    await db.SaveChangesAsync();
    var store = new FuelExchangeRateStore(db, new PlanningPublicationScope(db));

    Assert.Null(await store.ReadAsync(default));
    Assert.True(await store.AcquireAsync("repair", now, default));
    await store.SaveAsync("repair", rate, default);
    Assert.Equal(rate, await store.ReadAsync(default));
  }

  [Fact]
  public async Task DedicatedLeasePersistsDecimalRateAndRejectsStaleOwners()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var store = new FuelExchangeRateStore(db, new PlanningPublicationScope(db));
    var now = DateTime.UtcNow;
    var rate = new FuelExchangeRate(
      1m / 1.3917m,
      DateOnly.FromDateTime(now).AddDays(-1),
      now
    );
    Assert.Null(await store.ReadAsync(default));
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => store.SaveAsync("first", rate, default)
    );
    Assert.True(await store.AcquireAsync("first", now, default));
    Assert.Null(await store.ReadAsync(default));
    Assert.False(await store.AcquireAsync("second", now, default));
    Assert.True(
      await new SynchronizationStore(db).AcquireAsync("fleet", now, default)
    );
    Assert.True(
      await new GmailWatchStore(db).AcquireAsync("gmail", now, default)
    );
    await store.SaveAsync("first", rate, default);
    await using (var restarted = new AppDbContext(options))
      Assert.Equal(
        rate,
        await new FuelExchangeRateStore(
          restarted,
          new PlanningPublicationScope(restarted)
        ).ReadAsync(default)
      );
    await db
      .SynchronizationCheckpoints.Where(x => x.Id == FuelExchangeRateStore.Id)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.LeaseUntil, now.AddSeconds(-1))
      );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => store.SaveAsync("first", rate, default)
    );
    Assert.True(await store.AcquireAsync("second", now, default));
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => store.SaveAsync("first", rate with { UsdPerCad = 1m }, default)
    );
    await store.ReleaseAsync("first", default);
    Assert.Equal(
      "second",
      await db
        .SynchronizationCheckpoints.Where(x => x.Id == FuelExchangeRateStore.Id)
        .Select(x => x.Owner)
        .SingleAsync()
    );
    Assert.Equal(rate, await store.ReadAsync(default));
    await store.ReleaseAsync("second", default);
    Assert.True(await store.AcquireAsync("third", DateTime.UtcNow, default));
    var independentOwners = await db
      .SynchronizationCheckpoints.Where(x => x.Id != FuelExchangeRateStore.Id)
      .Select(x => x.Owner)
      .OrderBy(x => x)
      .ToArrayAsync();
    Assert.Equal(["fleet", "gmail"], independentOwners);
  }
}
