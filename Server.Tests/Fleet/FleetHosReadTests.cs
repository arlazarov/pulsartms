using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Services;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class FleetHosReadTests
{
  [Fact]
  public async Task ColdReadDemandsSharedSnapshotAndAssignmentChangesNeverReusePreviousDriverClocks()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "first",
      Name = "First driver",
    };
    var next = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "second",
      Name = "Second driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11007",
      ExternalId = "truck",
      IsActive = true,
      Driver = driver,
    };
    db.Drivers.Add(next);
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time);
    var handler = new GetFleetHosHandler(db, snapshot, new DriverHosStore(db));
    Assert.Null(
      Assert.Single((await handler.Handle(new(), default)).Response!).Value.Hos
    );
    Assert.True(snapshot.TryBeginRefresh(false));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        [driver.ExternalId] = new()
        {
          UpdatedAt = time.GetUtcNow().UtcDateTime,
          DriveMs = 3600000,
        },
      }
    );
    Assert.Equal(
      3600000,
      (await handler.Handle(new([truck.Id]), default))
        .Response![truck.Id]
        .Hos!
        .DriveMs
    );
    truck.Driver = next;
    await db.SaveChangesAsync();
    var changed = (await handler.Handle(new([truck.Id]), default)).Response![
      truck.Id
    ];
    Assert.Equal("Second driver", changed.DriverName);
    Assert.Null(changed.Hos);
    Assert.Empty(
      (await handler.Handle(new([Guid.NewGuid()]), default)).Response!
    );
    truck.Driver = driver;
    await db.SaveChangesAsync();
    time.Advance(TimeSpan.FromMinutes(1));
    Assert.Null(
      (await handler.Handle(new([truck.Id]), default)).Response![truck.Id].Hos
    );
  }
}
