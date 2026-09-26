using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Queries;
using Application.Features.Fleet.Services;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Eta;

// How long a driver has been in their current status, as a conversation
// shows it: from the driver's HOS history, under the forecasts' rule,
// whether or not a forecast read it first. The clocks' fetch time is never
// a status start.
[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class DriverDutyStatusReadTests
{
  [Fact]
  public async Task TheStatusStartComesOnlyFromCurrentCachedHistory()
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
      Name = "Driver",
    };
    var unlinked = new Driver { Id = Guid.NewGuid(), Name = "No provider id" };
    db.Drivers.AddRange(driver, unlinked);
    await db.SaveChangesAsync();
    var time = new ManualTimeProvider();
    var now = time.GetUtcNow();
    var snapshot = new DriverHosSnapshot(time, new TestCompany());
    var history = new CachedHistory();
    // Each read is its own request, with its own clock reader.
    async Task<DriverDutyView> Read(Guid id) =>
      (
        await new GetDriverDutyStatusHandler(
          new DriverClockReader(db, snapshot, new DriverHosStore(db)),
          history,
          null!,
          time
        ).Handle(new(id), default)
      ).Response!;

    Assert.Equal(DriverDutyView.Unknown, await Read(driver.Id));
    Assert.True(snapshot.TryBeginRefresh(false));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        [driver.ExternalId] = new()
        {
          UpdatedAt = now.UtcDateTime,
          CurrentDutyStatus = "driving",
        },
      }
    );
    HosHistory Through(DateTimeOffset through, string latest) =>
      new(
        now.AddDays(-2),
        through,
        "Etc/UTC",
        0,
        null,
        null,
        [
          new(now.AddHours(-5), now.AddHours(-2), "offDuty"),
          new(now.AddHours(-2), now.AddMinutes(-80), "onDuty"),
          new(now.AddMinutes(-80), through, latest),
        ]
      );

    // Clocks alone: the status, and no start.
    Assert.Equal(new("driving", null), await Read(driver.Id));

    history.Value = Through(now, "driving");
    Assert.Equal(new("driving", now.AddMinutes(-80)), await Read(driver.Id));

    // History older than the rule trusts: no start, not the old one.
    history.Value = Through(now.AddMinutes(-10), "driving");
    Assert.Equal(new("driving", null), await Read(driver.Id));

    // History and clocks disagree: no start.
    history.Value = Through(now, "onDuty");
    Assert.Equal(new("driving", null), await Read(driver.Id));

    Assert.Equal(DriverDutyView.Unknown, await Read(unlinked.Id));
    Assert.Equal(5, history.Reads);
  }

  // The provider's own gate and one-minute cache bound what these reads
  // cost (SamsaraHosHistoryTests); here each read is counted.
  private sealed class CachedHistory : IHosHistoryProvider
  {
    public HosHistory? Value { get; set; }
    public int Reads { get; private set; }

    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
    {
      Reads++;
      return Task.FromResult(Value);
    }
  }
}
