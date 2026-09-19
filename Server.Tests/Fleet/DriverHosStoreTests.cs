using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

// Hours are served from a snapshot a background operation fills, so an
// instance that does not run that operation, or one that has just restarted,
// held nothing and reported no hours at all. The readings now outlive the
// process that fetched them.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class DriverHosStoreTests
{
  [Fact]
  public async Task AnInstanceWithoutTheRefreshStillReportsTheLastReadings()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Store.WriteAsync(Clocks("driver-1", 3600_000), default);

    // A fresh snapshot holds nothing: this is the instance that serves
    // requests while another runs the refresh.
    var result = await new GetFleetHosHandler(
      f.Db,
      new DriverHosSnapshotStub(),
      f.Store
    ).Handle(new(), default);

    var hos = Assert.Single(result.Response!).Value.Hos;
    Assert.NotNull(hos);
    Assert.Equal(3600_000, hos!.DriveMs);
  }

  [Fact]
  public async Task AReadingTheProviderStopsReportingDoesNotSurvive()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Store.WriteAsync(Clocks("driver-1", 1000), default);

    await f.Store.WriteAsync(Clocks("driver-2", 2000), default);

    var stored = await f.Store.ReadAsync(default);
    Assert.Equal("driver-2", Assert.Single(stored).Key);
  }

  [Fact]
  public async Task RepeatedWritesReplaceTheReadingRatherThanAccumulate()
  {
    await using var f = await Fixture.CreateAsync();

    await f.Store.WriteAsync(Clocks("driver-1", 1000), default);
    await f.Store.WriteAsync(Clocks("driver-1", 7000), default);

    Assert.Equal(
      7000,
      Assert.Single(await f.Store.ReadAsync(default)).Value.DriveMs
    );
    Assert.Equal(1, await f.Db.DriverHosReadings.CountAsync());
  }

  [Fact]
  public async Task TheObservationTimeIsTheProvidersNotTheWrites()
  {
    await using var f = await Fixture.CreateAsync();
    var observed = new DateTime(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);

    await f.Store.WriteAsync(
      new Dictionary<string, DriverHosClocks>
      {
        ["driver-1"] = new() { DriveMs = 100, UpdatedAt = observed },
      },
      default
    );

    var row = await f.Db.DriverHosReadings.SingleAsync();
    Assert.Equal(observed, row.ObservedAt);
    Assert.NotEqual(observed, row.RecordedAt);
  }

  // Hours decide whether a driver may legally drive. A reading nobody has
  // refreshed for long enough is absence of information, not current hours.
  [Theory]
  [InlineData(5, true)]
  [InlineData(14, true)]
  [InlineData(16, false)]
  [InlineData(120, false)]
  public async Task AReadingIsOnlyAnsweredWithWhileItIsRecent(
    int ageMinutes,
    bool answered
  )
  {
    await using var f = await Fixture.CreateAsync();
    await f.Store.WriteAsync(
      new Dictionary<string, DriverHosClocks>
      {
        ["driver-1"] = new()
        {
          DriveMs = 100,
          UpdatedAt = DateTime.UtcNow.AddMinutes(-ageMinutes),
        },
      },
      default
    );

    var stored = await f.Store.ReadAsync(default);

    Assert.Equal(answered, stored.ContainsKey("driver-1"));
  }

  [Fact]
  public async Task AnInstanceWithoutTheRefreshReportsNoHoursRatherThanOldOnes()
  {
    await using var f = await Fixture.CreateAsync();
    await f.Store.WriteAsync(
      new Dictionary<string, DriverHosClocks>
      {
        ["driver-1"] = new()
        {
          DriveMs = 100,
          UpdatedAt = DateTime.UtcNow.AddHours(-2),
        },
      },
      default
    );

    var result = await new GetFleetHosHandler(
      f.Db,
      new DriverHosSnapshotStub(),
      f.Store
    ).Handle(new(), default);

    Assert.Null(Assert.Single(result.Response!).Value.Hos);
  }

  private static Dictionary<string, DriverHosClocks> Clocks(
    string driver,
    long driveMs
  ) =>
    new()
    {
      [driver] = new()
      {
        DriveMs = driveMs,
        UpdatedAt = DateTime.UtcNow,
        CurrentDutyStatus = "Driving",
      },
    };

  private sealed class DriverHosSnapshotStub
    : Application.Features.Fleet.Interfaces.IDriverHosProvider
  {
    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    ) =>
      Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
        new Dictionary<string, DriverHosClocks>()
      );
  }

  private sealed class Fixture : IAsyncDisposable
  {
    private SqliteConnection Connection { get; init; } = null!;
    public required AppDbContext Db { get; init; }
    public required DriverHosStore Store { get; init; }

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
      db.Drivers.Add(driver);
      db.Trucks.Add(
        new Truck
        {
          Id = Guid.NewGuid(),
          ExternalId = "hos-store",
          UnitNumber = "1",
          IsActive = true,
          Driver = driver,
        }
      );
      await db.SaveChangesAsync();
      return new Fixture
      {
        Connection = connection,
        Db = db,
        Store = new DriverHosStore(db),
      };
    }

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      await Connection.DisposeAsync();
    }
  }
}
