using Application.Features.Eta.Services;
using Application.Features.Fleet.Services;
using Domain.Models.Eta;
using Domain.Models.Fleet;

namespace Server.Tests.Eta;

// Forecasts are checked every two minutes, not every ten seconds, and a
// forecast is due the moment it expires rather than a whole interval later.
// An event that changes a forecast - a route, a stop, an assignment, a duty
// status - wakes the worker at once. Time here is moved by hand; nothing
// waits on a real clock.
[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaRefreshCycleTests
{
  private static readonly DateTime Start = new(
    2026,
    9,
    23,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public void AForecastIsDueWhenItExpiresNotAWholeIntervalLater()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var id = Guid.NewGuid();
    memory.View(id, Start);
    memory.Results[id] = Entry(Start, Start + EtaMemory.RefreshInterval);

    Assert.Equal(
      Start + EtaMemory.RefreshInterval,
      memory.NextCheck(Start.AddSeconds(1))
    );
    Assert.DoesNotContain(
      id,
      memory.Due(Start + EtaMemory.RefreshInterval - TimeSpan.FromSeconds(1))
    );
    Assert.Contains(id, memory.Due(Start + EtaMemory.RefreshInterval));

    var later = Start + EtaMemory.RefreshInterval;
    memory.Results[id] = Entry(later, later + EtaMemory.RefreshInterval);
    Assert.Equal(
      later + EtaMemory.RefreshInterval,
      memory.NextCheck(later.AddSeconds(1))
    );
  }

  [Fact]
  public void TheEarliestExpiryBringsTheCheckForward()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var soon = Guid.NewGuid();
    var later = Guid.NewGuid();
    memory.View(soon, Start);
    memory.View(later, Start);
    memory.Results[soon] = Entry(Start, Start.AddSeconds(40));
    memory.Results[later] = Entry(Start, Start.AddSeconds(110));
    Assert.Equal(Start.AddSeconds(40), memory.NextCheck(Start));
  }

  [Fact]
  public void WithNothingToExpireTheWorkerSleepsTheWholeInterval()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    Assert.Equal(Start + EtaMemory.RefreshInterval, memory.NextCheck(Start));
  }

  [Fact]
  public async Task AFailedRefreshIsRetriedAtTheIntervalNotInALoop()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var id = Guid.NewGuid();
    memory.View(id, Start);
    // Consume the first view's wake-up.
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    // Expired and never replaced: due, but not a reason to spin.
    memory.Results[id] = Entry(Start.AddMinutes(-3), Start.AddMinutes(-1));
    Assert.Contains(id, memory.Due(Start));
    Assert.Equal(Start + EtaMemory.RefreshInterval, memory.NextCheck(Start));
    using var stop = new CancellationTokenSource();
    var waiting = memory.WaitForRefreshAsync(stop.Token);
    Assert.False(waiting.IsCompleted);
    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
  }

  [Fact]
  public async Task AnEventWakesTheWorkerWithoutWaitingForTheInterval()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var waiting = memory.WaitForRefreshAsync(default);
    Assert.False(waiting.IsCompleted);
    memory.RequestRefresh();
    await waiting.WaitAsync(TimeSpan.FromSeconds(1));
  }

  [Fact]
  public async Task ADutyChangeMakesOnlyThatDriversForecastsDue()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var mine = Guid.NewGuid();
    var theirs = Guid.NewGuid();
    memory.View(mine, Start);
    memory.View(theirs, Start);
    await memory
      .WaitForRefreshAsync(default)
      .WaitAsync(TimeSpan.FromSeconds(1));
    var valid = Start + EtaMemory.RefreshInterval;
    memory.Results[mine] = Entry(Start, valid) with { Driver = "A" };
    memory.Results[theirs] = Entry(Start, valid) with { Driver = "B" };
    Assert.Empty(memory.Due(Start));

    var waiting = memory.WaitForRefreshAsync(default);
    memory.DutyChanged(["A"]);

    await waiting.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal([mine], memory.Due(Start));
    Assert.False(memory.Results[theirs].Superseded);
  }

  [Fact]
  public void HoursRaiseADutyChangeOnlyForAChangeOrAFirstReading()
  {
    var time = new ManualTimeProvider(Start);
    var snapshot = new DriverHosSnapshot(time, new TestCompany());
    var raised = new List<string[]>();
    snapshot.DutyChanged += drivers => raised.Add(drivers.Order().ToArray());

    snapshot.Complete(Clocks(("A", "driving", 600)));
    snapshot.Complete(Clocks(("A", "driving", 540), ("B", "offDuty", 0)));
    snapshot.Complete(Clocks(("A", "offDuty", 540), ("B", "offDuty", 0)));
    // Clocks counting down under the same duty status change nothing.
    snapshot.Complete(Clocks(("A", "offDuty", 480), ("B", "offDuty", 0)));
    // A failed read is not a reading.
    snapshot.Complete(null);

    Assert.Equal(new[] { new[] { "A" }, ["B"], ["A"] }, raised);
  }

  private static EtaMemory.Entry Entry(DateTime calculated, DateTime valid) =>
    new("signature", new DispatchEta(calculated, valid, [], null, []), "road")
    {
      WorkKey = "work",
    };

  private static Dictionary<string, DriverHosClocks> Clocks(
    params (string Driver, string Duty, long DriveMs)[] drivers
  ) =>
    drivers.ToDictionary(
      x => x.Driver,
      x => new DriverHosClocks
      {
        DriveMs = x.DriveMs,
        UpdatedAt = Start,
        CurrentDutyStatus = x.Duty,
      }
    );
}
