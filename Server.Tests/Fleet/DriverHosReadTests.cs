using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Services;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fleet;

// One driver's hours, as a conversation shows them: from the shared
// snapshot, keyed by the driver, so a co-driver never shows the truck
// driver's clocks; unknown, never zero, when the snapshot has none.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class DriverHosReadTests
{
  [Fact]
  public async Task ADriversOwnClocksOrHonestlyNone()
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
      Name = "Truck driver",
    };
    var codriver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "second",
      Name = "Co-driver",
    };
    db.Trucks.Add(
      new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = "11007",
        ExternalId = "truck",
        IsActive = true,
        Driver = driver,
      }
    );
    db.Drivers.Add(codriver);
    await db.SaveChangesAsync();
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time, new TestCompany());
    // Each read is its own request, with its own clock reader.
    GetDriverHosHandler handler() =>
      new(new DriverClockReader(db, snapshot, new DriverHosStore(db)));

    Assert.Equal(
      DriverHoursView.Unknown,
      (await handler().Handle(new(driver.Id), default)).Response
    );

    Assert.True(snapshot.TryBeginRefresh(false));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        [driver.ExternalId] = new()
        {
          UpdatedAt = time.GetUtcNow().UtcDateTime,
          DriveMs = 3600000,
          ShiftMs = 7200000,
          CurrentDutyStatus = "driving",
        },
      }
    );
    var own = (await handler().Handle(new(driver.Id), default)).Response!;
    Assert.Equal(
      (true, 3600000L, 7200000L, "driving"),
      (own.Known, own.DriveMs!.Value, own.ShiftMs!.Value, own.DutyStatus)
    );
    Assert.Equal(time.GetUtcNow().UtcDateTime, own.UpdatedAt);
    Assert.Equal(
      DriverHoursView.Unknown,
      (await handler().Handle(new(codriver.Id), default)).Response
    );
    Assert.Equal(
      DriverHoursView.Unknown,
      (await handler().Handle(new(Guid.NewGuid()), default)).Response
    );
  }
}
