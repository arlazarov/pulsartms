using System.Data.Common;
using Application.Caching;
using Application.Features.Synchronization.Options;
using Application.Reference;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Server.Tests.Caching;

[Trait("Category", "Architecture")]
public class FleetNamesTests
{
  [Fact]
  public async Task NamesAreReadOnceAcrossRequestsAndAgainWhenTheCatalogChanges()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var counter = new Counter();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(counter)
        .Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { ExternalId = "t", UnitNumber = "11007" };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );

    counter.Reads = 0;
    var first = new FleetNames(db, reads);
    Assert.Equal("11007", (await first.TrucksAsync(default))[truck.Id]);
    Assert.Empty(await first.DriversAsync(default));
    Assert.Empty(await first.TrailersAsync(default));
    Assert.Equal(3, counter.Reads);

    // A second request: a new scoped instance, the same cache.
    var second = new FleetNames(db, reads);
    Assert.Equal("11007", (await second.TrucksAsync(default))[truck.Id]);
    Assert.Empty(await second.TrailersAsync(default));
    Assert.Equal(3, counter.Reads);

    // What both writers of these names do after they change one.
    truck.UnitNumber = "11008";
    await db.SaveChangesAsync();
    reads.Invalidate("fleet-catalog");

    counter.Reads = 0;
    var third = new FleetNames(db, reads);
    Assert.Equal("11008", (await third.TrucksAsync(default))[truck.Id]);
    Assert.Equal(3, counter.Reads);
    // The request that was already running keeps the names it started with.
    Assert.Equal("11007", (await second.TrucksAsync(default))[truck.Id]);
  }

  [Fact]
  public async Task WithoutACacheEachRequestStillReadsOnlyOnce()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var counter = new Counter();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(counter)
        .Options
    );
    await db.Database.EnsureCreatedAsync();
    counter.Reads = 0;
    var names = new FleetNames(db);
    await names.TrucksAsync(default);
    await names.DriversAsync(default);
    await names.TrucksAsync(default);
    Assert.Equal(3, counter.Reads);
  }

  private sealed class Counter : DbCommandInterceptor
  {
    public int Reads;

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Reads++;
      return base.ReaderExecutingAsync(
        command,
        eventData,
        result,
        cancellationToken
      );
    }
  }
}
