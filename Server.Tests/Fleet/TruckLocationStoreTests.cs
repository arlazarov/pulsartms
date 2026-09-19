using Application.Features.Fleet.Models;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fleet;

// Positions are held in the snapshot of whichever process collected them, so
// an instance that does not collect telemetry drew an empty map. They now
// outlive that process, without a slower reader undoing a fresher position.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class TruckLocationStoreTests
{
  [Fact]
  public async Task ARecordedPositionIsReadBackWithItsTruckIdentity()
  {
    await using var f = await Fixture.CreateAsync();

    await f.Store.WriteAsync([f.Position(minutesAgo: 1)], default);

    var read = Assert.Single(await f.Store.ReadAsync(default));
    Assert.Equal(f.TruckId, read.TruckId);
    Assert.Equal("11007", read.UnitNumber);
    Assert.Equal("Test Driver", read.DriverName);
    Assert.Equal(40.5m, read.Latitude);
  }

  [Fact]
  public async Task AnOlderObservationNeverReplacesANewerOne()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Store.WriteAsync(
      [f.Position(minutesAgo: 1, latitude: 41m)],
      default
    );

    await f.Store.WriteAsync(
      [f.Position(minutesAgo: 30, latitude: 39m)],
      default
    );

    Assert.Equal(41m, Assert.Single(await f.Store.ReadAsync(default)).Latitude);
  }

  [Theory]
  [InlineData(60, true)]
  [InlineData(359, true)]
  [InlineData(361, false)]
  public async Task OnlyAPositionRecentEnoughToDrawIsAnsweredWith(
    int minutesAgo,
    bool answered
  )
  {
    await using var f = await Fixture.CreateAsync();

    await f.Store.WriteAsync([f.Position(minutesAgo)], default);

    Assert.Equal(answered, (await f.Store.ReadAsync(default)).Count == 1);
  }

  [Fact]
  public async Task AStaleSensorDoesNotOverwriteAFresherReading()
  {
    await using var f = await Fixture.CreateAsync();
    var fresh = f.Position(minutesAgo: 5);
    fresh.FuelPercent = 80;
    fresh.FuelUpdatedAt = DateTime.UtcNow.AddMinutes(-5);
    await f.Store.WriteAsync([fresh], default);

    var later = f.Position(minutesAgo: 1);
    later.FuelPercent = 20;
    later.FuelUpdatedAt = DateTime.UtcNow.AddHours(-3);
    await f.Store.WriteAsync([later], default);

    Assert.Equal(
      80,
      Assert.Single(await f.Store.ReadAsync(default)).FuelPercent
    );
  }

  private sealed class Fixture : IAsyncDisposable
  {
    private SqliteConnection Connection { get; init; } = null!;
    public required AppDbContext Db { get; init; }
    public required TruckLocationStore Store { get; init; }
    public required Guid TruckId { get; init; }

    public TruckLocation Position(int minutesAgo, decimal latitude = 40.5m) =>
      new()
      {
        TruckId = TruckId,
        Latitude = latitude,
        Longitude = -80m,
        Speed = 55m,
        EngineState = "On",
        ObservedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
        UpdatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
      };

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = "driver-1",
        Name = "Test Driver",
      };
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "location-store",
        UnitNumber = "11007",
        IsActive = true,
        Driver = driver,
      };
      db.Drivers.Add(driver);
      db.Trucks.Add(truck);
      await db.SaveChangesAsync();
      return new Fixture
      {
        Connection = connection,
        Db = db,
        Store = new TruckLocationStore(db),
        TruckId = truck.Id,
      };
    }

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      await Connection.DisposeAsync();
    }
  }
}
