using Application.Features.Fuel.Models;
using Domain.Entities.Fuel;
using Infrastructure.Integrations.Google.Places;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelStationLookupStoreTests
{
  [Fact]
  public async Task ReservationSurvivesContextDisposalAndImportRollbackAndRejectsStaleOwner()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
    await using var provider = services.BuildServiceProvider();
    var scopes = provider.GetRequiredService<IServiceScopeFactory>();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var first = new FuelStationLookupStore(db, scopes);
    var now = DateTime.UtcNow;
    Assert.True(await first.AcquireAsync("station", "first", now, default));
    await first.SaveAsync("station", "first", new() { Signature = "query", Failures = 1, NextAttemptAt = now.AddMinutes(15) }, default);
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      db.FuelStations.Add(new FuelStation { Id = Guid.NewGuid(), ExternalId = "rolled-back" });
      await db.SaveChangesAsync();
      await transaction.RollbackAsync();
    }
    var second = new FuelStationLookupStore(db, scopes);
    Assert.Equal(now.AddMinutes(15), (await second.ReadAsync("STATION", default))!.NextAttemptAt);
    Assert.Equal(0, await db.FuelStations.CountAsync());
    Assert.False(await second.AcquireAsync("STATION", "second", now, default));
    Assert.True(await new SynchronizationStore(db).AcquireAsync("fleet", now, default));
    await db.SynchronizationCheckpoints.Where(x => x.Owner == "first")
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.AddSeconds(-1)));
    Assert.True(await second.AcquireAsync("station", "second", now, default));
    await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveAsync("station", "first", new FuelStationLookupState(), default));
    await first.ReleaseAsync("station", "first", default);
    Assert.Equal("second", await db.SynchronizationCheckpoints.Where(x => x.Id != SynchronizationStore.Id).Select(x => x.Owner).SingleAsync());
  }
}
