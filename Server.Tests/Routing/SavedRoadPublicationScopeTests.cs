using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class SavedRoadPublicationScopeTests
{
  [Theory]
  [InlineData("base", false)]
  [InlineData("base", true)]
  [InlineData("live", false)]
  [InlineData("live", true)]
  [InlineData("connection", false)]
  [InlineData("connection", true)]
  public async Task SQLiteExcludesIndependentRoadWritersUntilCommit(
    string kind,
    bool insert
  )
  {
    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = "planning-roads-" + Guid.NewGuid(),
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared,
      DefaultTimeout = 1,
    }.ToString();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    var load = new Load { Id = Guid.NewGuid(), LoadNumber = 1 };
    var previous = new Load { Id = Guid.NewGuid(), LoadNumber = 2 };
    db.Trucks.Add(truck);
    db.Dispatches.AddRange(load, previous);
    await db.SaveChangesAsync();
    object road = kind switch
    {
      "base" => new DispatchBaseRoute
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        RouteJson = "{}",
      },
      "live" => new DispatchRoutePlan
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        TruckId = truck.Id,
        PlanJson = "{}",
      },
      _ => new DispatchDeadhead
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        PreviousDispatchId = previous.Id,
        RouteJson = "{}",
      },
    };
    if (!insert)
    {
      db.Add(road);
      await db.SaveChangesAsync();
      db.ChangeTracker.Clear();
    }
    await using var writer = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .Options
    );
    if (insert)
      writer.Add(road);
    else
    {
      writer.Attach(road);
      writer
        .Entry(road)
        .Property(kind == "live" ? "PlanJson" : "RouteJson")
        .CurrentValue = "updated";
    }
    Task<string?> Read() =>
      kind switch
      {
        "base" => db
          .DispatchBaseRoutes.Select(x => x.RouteJson)
          .SingleOrDefaultAsync(),
        "live" => db
          .DispatchRoutePlans.Select(x => x.PlanJson)
          .SingleOrDefaultAsync(),
        _ => db
          .DispatchDeadheads.Select(x => x.RouteJson)
          .SingleOrDefaultAsync(),
      };

    await using (
      var transaction = await new PlanningPublicationScope(db).BeginAsync(
        null,
        default
      )
    )
    {
      var expected = insert ? null : "{}";
      Assert.Equal(expected, await Read());
      var error = await Assert.ThrowsAsync<DbUpdateException>(
        () => writer.SaveChangesAsync()
      );
      Assert.Contains(
        Assert.IsType<SqliteException>(error.InnerException).SqliteErrorCode,
        new[] { 5, 6 }
      );
      Assert.Equal(expected, await Read());
      await transaction.CommitAsync();
    }

    await writer.SaveChangesAsync();
    Assert.Equal(insert ? "{}" : "updated", await Read());
  }
}
