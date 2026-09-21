using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Rules.Eta;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class HosPartialCycleTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    13,
    14,
    0,
    0,
    TimeSpan.Zero
  );

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void KnownCycleSurvivesMissingOlderDays(bool leadingGap)
  {
    var history = History(leadingGap);
    var ledger = new HosCycleFeasibility(Now, Clocks(), "US", history);
    Assert.True(ledger.Verified);
    Assert.Equal(3837, ledger.BalanceMinutes(Now));
    Assert.Null(ledger.UnavailableReason);
    Assert.False(ledger.RecapVerified);

    ledger.Observe(Now, Now.AddHours(1), "driving");
    ledger.Observe(Now.AddHours(1), Now.AddHours(1.25), "onDuty");
    ledger.Observe(Now.AddHours(1.25), Now.AddHours(1.5), "yardMove");
    ledger.Observe(Now.AddHours(1.5), Now.AddDays(2), "sleeperBerth");

    Assert.Equal(3747, ledger.BalanceMinutes(Now.AddDays(2)));
    var snapshot = Assert.IsType<StopCycleForecast>(
      ledger.Snapshot(Now.AddDays(2))
    );
    Assert.Equal(3747, snapshot.RemainingMinutes);
    Assert.False(snapshot.RecapVerified);
    Assert.Null(snapshot.NextRecapAt);
    Assert.Null(snapshot.NextRecapMinutes);
    Assert.Null(ledger.NextUsableRecap(Now));
  }

  [Fact]
  public void PartialTimelineDoesNotEnableDailyHistoryCredits()
  {
    var history = History(true);
    Assert.Null(HosTimeline.Create(history, Now));
    var clock = new HosTravelClock(Now, Clocks(), "US", history);
    Assert.False(clock.HistoryAvailable);
    clock.Drive(2, "US");
    Assert.True(clock.CycleFeasibility.Verified);
    Assert.Equal(3717, clock.CycleFeasibility.BalanceMinutes(clock.Now));
    Assert.Equal(0, clock.RecapWaits);
    Assert.Equal(0, clock.SplitRests);
  }

  [Fact]
  public void MissingOlderDaysStillExposeAnActualProjectedDrivingShortage()
  {
    var clocks = Clocks();
    clocks.CycleMs = 30 * 60_000L;
    var ledger = new HosCycleFeasibility(Now, clocks, "US", History(true));
    ledger.Observe(Now, Now.AddHours(1), "driving");
    Assert.Equal(-30, ledger.BalanceMinutes(Now.AddHours(1)));
    Assert.Equal(Now.AddMinutes(30), ledger.FirstShortageAt);
    Assert.Equal(30, ledger.DrivingShortfallMinutes);
    Assert.Null(ledger.NextUsableRecap(Now));
  }

  [Theory]
  [InlineData("stale")]
  [InlineData("gap")]
  [InlineData("overlap")]
  [InlineData("unknown-status")]
  [InlineData("missing-rule")]
  [InlineData("empty")]
  public void InvalidRecentEvidenceRemainsUnavailable(string reason)
  {
    var history = History(true);
    history = reason switch
    {
      "stale" => history with { Through = Now.AddMinutes(-4) },
      "gap" => history with
      {
        Periods =
        [
          new(Now.AddHours(-8), Now.AddHours(-4), "offDuty"),
          new(Now.AddHours(-3), Now, "offDuty"),
        ],
      },
      "overlap" => history with
      {
        Periods =
        [
          new(Now.AddHours(-8), Now.AddHours(-3), "offDuty"),
          new(Now.AddHours(-4), Now, "driving"),
        ],
      },
      "unknown-status" => history with
      {
        Periods = [new(Now.AddHours(-8), Now, "unknown")],
      },
      "missing-rule" => history with { UsCycle = null },
      _ => history with { Periods = [] },
    };
    var ledger = new HosCycleFeasibility(Now, Clocks(), "US", history);
    Assert.False(ledger.Verified);
    Assert.Null(ledger.BalanceMinutes(Now));
    Assert.Null(ledger.NextUsableRecap(Now));
  }

  [Theory]
  [InlineData(null)]
  [InlineData(-1L)]
  [InlineData(71 * 3_600_000L)]
  public void InvalidCurrentCycleCannotBeReplacedByPartialHistory(long? cycle)
  {
    var clocks = Clocks();
    clocks.CycleMs = cycle;
    var ledger = new HosCycleFeasibility(Now, clocks, "US", History(true));
    Assert.False(ledger.Verified);
    Assert.Null(ledger.BalanceMinutes(Now));
  }

  [Fact]
  public void PartialHistoryCannotTransferHoursAcrossTheBorder()
  {
    var ledger = new HosCycleFeasibility(Now, Clocks(), "US", History(true));
    ledger.Enter("CA");
    ledger.Enter("US");
    Assert.False(ledger.Verified);
    Assert.Null(ledger.BalanceMinutes(Now));
  }

  [Fact]
  public void CanadaCycleTwoStillRequiresItsAdditionalRestHistory()
  {
    var history = History(true) with
    {
      CanadaCycle = new(14, 120, 72),
      Periods = [new(Now.AddHours(-4), Now, "onDuty")],
    };
    var ledger = new HosCycleFeasibility(Now, Clocks(), "CA", history);
    Assert.False(ledger.Verified);
    Assert.Null(ledger.BalanceMinutes(Now));
  }

  private static HosHistory History(bool leadingGap) =>
    new(
      Now.AddDays(leadingGap ? -16 : -1),
      Now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      new(7, 70, 36),
      [new(Now.AddDays(-1), Now, "offDuty")]
    );

  private static DriverHosClocks Clocks() =>
    new()
    {
      CycleMs = 3837 * 60_000L,
      DriveMs = 11 * 3_600_000L,
      ShiftMs = 14 * 3_600_000L,
      BreakMs = 8 * 3_600_000L,
      UpdatedAt = Now.UtcDateTime,
    };
}
