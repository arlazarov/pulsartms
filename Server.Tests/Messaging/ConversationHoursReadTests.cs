using System.Data.Common;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Queries;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Services;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Messaging;

// A conversation's context asks for one driver's clocks and duty status in
// one request. They share one read of the driver and one copy of the clock
// snapshot; a cold instance reads the stored clocks once, not once per
// handler. Each request reads afresh: nothing is kept between requests.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ConversationHoursReadTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ClocksAndDutyShareOneDriverReadAndOneSnapshot(bool warm)
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
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "d-1",
      Name = "Driver",
    };
    db.Drivers.Add(driver);
    await db.SaveChangesAsync();
    var clocks = new Dictionary<string, DriverHosClocks>
    {
      [driver.ExternalId] = new()
      {
        UpdatedAt = DateTime.UtcNow,
        CurrentDutyStatus = "driving",
        DriveMs = 3600000,
      },
    };
    var provider = new CountingProvider(warm ? clocks : []);
    var store = new CountingStore(clocks);
    var history = new CountingHistory();

    for (var request = 1; request <= 2; request++)
    {
      counter.Reads = 0;
      // One request: its own reader, both handlers.
      var reader = new DriverClockReader(db, provider, store);
      var hours = await new GetDriverHosHandler(reader).Handle(
        new(driver.Id),
        default
      );
      var duty = await new GetDriverDutyStatusHandler(
        reader,
        history,
        null!,
        TimeProvider.System
      ).Handle(new(driver.Id), default);

      Assert.True(hours.Response!.Known);
      Assert.Equal("driving", duty.Response!.Status);
      Assert.Equal(1, counter.Reads);
      Assert.Equal(request, provider.Reads);
      Assert.Equal(warm ? 0 : request, store.Reads);
      Assert.Equal(request, history.Reads);
    }
  }

  private sealed class CountingProvider(
    IReadOnlyDictionary<string, DriverHosClocks> value
  ) : IDriverHosProvider
  {
    public int Reads;

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    )
    {
      Reads++;
      return Task.FromResult(value);
    }
  }

  private sealed class CountingStore(
    IReadOnlyDictionary<string, DriverHosClocks> value
  ) : IDriverHosStore
  {
    public int Reads;

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> ReadAsync(
      CancellationToken ct
    )
    {
      Reads++;
      return Task.FromResult(value);
    }

    public Task WriteAsync(
      IReadOnlyDictionary<string, DriverHosClocks> clocks,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }

  private sealed class CountingHistory : IHosHistoryProvider
  {
    public int Reads;

    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
    {
      Reads++;
      return Task.FromResult<HosHistory?>(null);
    }
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
