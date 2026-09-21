using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Rules.Eta;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class HosCycleAnchorTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    8,
    23,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public void ExactEldSecondsArePreservedWhenHistoryDiffersByThirtyThreeMinutes()
  {
    const long historySeconds = 49 * 3600 + 29 * 60 + 32;
    const long eldSeconds = 50 * 3600 + 2 * 60 + 38;
    var history = HosForecastFixture.History(
      Now,
      cycleHours: historySeconds / 3600d
    );
    var clocks = HosForecastFixture.Clocks(Now, history);
    Assert.Equal(historySeconds * 1000, clocks.CycleMs);
    clocks.CycleMs = eldSeconds * 1000;
    var ledger = new HosCycleFeasibility(Now, clocks, "US", history);

    Assert.True(ledger.Verified);
    Assert.False(ledger.RecapVerified);
    Assert.Equal(eldSeconds / 3600d, ledger.BalanceHours(Now)!.Value, 9);
    Assert.Equal(3002, ledger.BalanceMinutes(Now));
    AssertUnknownRecap(ledger, Now, 3002);
  }

  [Fact]
  public void PlannedDutyMergingWithTheLastHistoricalPeriodDebitsOnlyTheNewHour()
  {
    var (ledger, history) = Ledger(-33);
    Assert.Equal("onDuty", history.Periods[^1].Status);
    Assert.Equal(Now, history.Periods[^1].End);
    Assert.Equal(567, ledger.BalanceMinutes(Now));

    ledger.Observe(Now, Now.AddHours(1), "onDuty");

    Assert.Equal(507, ledger.BalanceMinutes(Now.AddHours(1)));
    AssertUnknownRecap(ledger, Now.AddHours(1), 507);
    Assert.Null(ledger.FirstShortageAt);
    Assert.Equal(0, ledger.DrivingShortfallMinutes);
  }

  [Fact]
  public void ExplicitRestartRestoresAvailableHoursWithoutErasingAnEarlierDrivingShortage()
  {
    var history = HosForecastFixture.History(
      Now,
      cycleHours: 1,
      firstDayHours: 3
    );
    var ledger = new HosCycleFeasibility(
      Now,
      OffsetClocks(history, -33),
      "US",
      history
    );
    ledger.Observe(Now, Now.AddHours(1), "driving");
    Assert.Equal(Now.AddMinutes(27), ledger.FirstShortageAt);
    Assert.Equal(33, ledger.DrivingShortfallMinutes);
    var restart = Now.AddHours(35);
    ledger.Observe(Now.AddHours(1), restart, "sleeperBerth");

    ledger.CreditRestart(restart);

    Assert.True(ledger.Verified);
    Assert.True(ledger.RecapVerified);
    Assert.Equal(70 * 60, ledger.BalanceMinutes(restart));
    Assert.Equal(
      70 * 60,
      Assert
        .IsType<StopCycleForecast>(ledger.Snapshot(restart))
        .RemainingMinutes
    );
    Assert.Equal(Now.AddMinutes(27), ledger.FirstShortageAt);
    Assert.Equal(33, ledger.DrivingShortfallMinutes);
  }

  [Theory]
  [InlineData(-33)]
  [InlineData(33)]
  public void CurrentEldAnchorsTheExactBalanceDespiteHistoryMismatch(
    int offsetMinutes
  )
  {
    var (ledger, _) = Ledger(offsetMinutes);

    Assert.True(ledger.Verified);
    Assert.False(ledger.RecapVerified);
    Assert.Equal(
      (600 + offsetMinutes) / 60d,
      ledger.BalanceHours(Now)!.Value,
      9
    );
    Assert.Equal(600 + offsetMinutes, ledger.BalanceMinutes(Now));
    AssertUnknownRecap(ledger, Now, 600 + offsetMinutes);
    Assert.Null(ledger.NextUsableRecap(Now));
    Assert.Null(ledger.FirstShortageAt);
    Assert.Equal(0, ledger.DrivingShortfallMinutes);
  }

  [Theory]
  [InlineData("driving", 120)]
  [InlineData("onDuty", 120)]
  [InlineData("yardMove", 120)]
  [InlineData("offDuty", 0)]
  [InlineData("sleeperBerth", 0)]
  [InlineData("personalConveyance", 0)]
  public void FutureDutyDebitsTheAnchorWithoutUnverifiedMidnightCredits(
    string status,
    int debitMinutes
  )
  {
    var (ledger, _) = Ledger(33);
    var end = Now.AddHours(2);

    ledger.Observe(Now, end, status);

    Assert.True(ledger.Verified);
    Assert.False(ledger.RecapVerified);
    Assert.Equal(633 - debitMinutes, ledger.BalanceMinutes(end));
    AssertUnknownRecap(ledger, end, 633 - debitMinutes);
    Assert.Null(ledger.NextUsableRecap(end));
  }

  [Fact]
  public void SignedShortageUsesTheEldAnchorAndSurvivesLaterRest()
  {
    var history = HosForecastFixture.History(
      Now,
      cycleHours: 1,
      firstDayHours: 3
    );
    var clocks = OffsetClocks(history, -33);
    var ledger = new HosCycleFeasibility(Now, clocks, "US", history);

    ledger.Observe(Now, Now.AddHours(1), "onDuty");
    Assert.Equal(-33, ledger.BalanceMinutes(Now.AddHours(1)));
    Assert.Equal(0, ledger.DrivingShortfallMinutes);
    Assert.Null(ledger.FirstShortageAt);

    ledger.Observe(Now.AddHours(1), Now.AddHours(1.5), "driving");
    ledger.Observe(Now.AddHours(1.5), Now.AddHours(12), "sleeperBerth");

    Assert.Equal(-63, ledger.BalanceMinutes(Now.AddHours(12)));
    Assert.Equal(63, ledger.DrivingShortfallMinutes);
    Assert.Equal(Now.AddHours(1), ledger.FirstShortageAt);
    AssertUnknownRecap(ledger, Now.AddHours(12), 0);
  }

  [Fact]
  public void ALongObservedRestDoesNotSilentlyCreditRestartOrHistoricalRecap()
  {
    var (ledger, _) = Ledger(-33);
    var end = Now.AddHours(34);

    ledger.Observe(Now, end, "sleeperBerth");

    Assert.Equal(567, ledger.BalanceMinutes(end));
    AssertUnknownRecap(ledger, end, 567);
    Assert.Null(ledger.NextUsableRecap(end));
  }

  [Fact]
  public void ExplicitCompletedRestartRestoresTheFullCycleAndVerifiedFutureCredits()
  {
    var (ledger, _) = Ledger(-33);
    var restart = Now.AddHours(34);
    ledger.Observe(Now, restart, "sleeperBerth");

    ledger.CreditRestart(restart);

    Assert.True(ledger.Verified);
    Assert.True(ledger.RecapVerified);
    Assert.Equal(70 * 60, ledger.BalanceMinutes(restart));
    var restarted = Assert.IsType<StopCycleForecast>(ledger.Snapshot(restart));
    Assert.Equal(70 * 60, restarted.RemainingMinutes);
    Assert.True(restarted.RecapVerified);
    Assert.Null(restarted.NextRecapAt);
    Assert.Null(restarted.NextRecapMinutes);

    ledger.Observe(restart, restart.AddHours(1), "onDuty");
    var worked = Assert.IsType<StopCycleForecast>(
      ledger.Snapshot(restart.AddHours(1))
    );
    Assert.Equal(69 * 60, worked.RemainingMinutes);
    Assert.True(worked.RecapVerified);
    Assert.Equal(
      new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero),
      worked.NextRecapAt
    );
    Assert.Equal(60, worked.NextRecapMinutes);
  }

  [Fact]
  public void IncompleteExplicitRestartCannotReplaceTheEldAnchor()
  {
    var (ledger, _) = Ledger(33);
    var incomplete = Now.AddHours(34).AddMinutes(-1);
    ledger.Observe(Now, incomplete, "sleeperBerth");

    Assert.Throws<InvalidOperationException>(
      () => ledger.CreditRestart(incomplete)
    );

    Assert.True(ledger.Verified);
    Assert.Equal(633, ledger.BalanceMinutes(incomplete));
    AssertUnknownRecap(ledger, incomplete, 633);
  }

  [Theory]
  [InlineData(-10, 770)]
  [InlineData(10, 780)]
  public void SmallVerifiedOffsetsDoNotDoubleCreditRecapOrOutliveTheirAnchorDay(
    int offsetMinutes,
    int firstRecapBalance
  )
  {
    var (ledger, _) = Ledger(offsetMinutes);
    Assert.True(ledger.Verified);
    Assert.True(ledger.RecapVerified);
    Assert.Equal(600 + offsetMinutes, ledger.BalanceMinutes(Now));

    ledger.Observe(Now, Now.AddHours(2), "offDuty");
    Assert.Equal(firstRecapBalance, ledger.BalanceMinutes(Now.AddHours(2)));
    ledger.Observe(Now.AddHours(2), Now.AddHours(3), "onDuty");
    Assert.Equal(
      firstRecapBalance - 60,
      ledger.BalanceMinutes(Now.AddHours(3))
    );

    var anchorExpiry = new DateTimeOffset(Now.Date.AddDays(8), TimeSpan.Zero);
    Assert.Equal(69 * 60, ledger.BalanceMinutes(anchorExpiry));
    var remainingProjectedWork = Assert.IsType<StopCycleForecast>(
      ledger.Snapshot(anchorExpiry)
    );
    Assert.Equal(69 * 60, remainingProjectedWork.RemainingMinutes);
    Assert.True(remainingProjectedWork.RecapVerified);
    Assert.Equal(anchorExpiry.AddDays(1), remainingProjectedWork.NextRecapAt);
    Assert.Equal(60, remainingProjectedWork.NextRecapMinutes);

    var allWorkExpired = anchorExpiry.AddDays(1);
    Assert.Equal(70 * 60, ledger.BalanceMinutes(allWorkExpired));
    var full = Assert.IsType<StopCycleForecast>(
      ledger.Snapshot(allWorkExpired)
    );
    Assert.Equal(70 * 60, full.RemainingMinutes);
    Assert.Null(full.NextRecapAt);
    Assert.Null(full.NextRecapMinutes);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("stale")]
  [InlineData("missing-rule")]
  public void CurrentEldDoesNotCertifyMissingHistoryOrRule(string unavailable)
  {
    var history = HosForecastFixture.History(Now, cycleHours: 10);
    var clocks = OffsetClocks(history, 33);
    var supplied = unavailable switch
    {
      "stale" => history with { Through = Now.AddMinutes(-4) },
      "missing-rule" => history with { UsCycle = null },
      _ => null,
    };
    var ledger = new HosCycleFeasibility(Now, clocks, "US", supplied);

    Assert.False(ledger.Verified);
    Assert.False(ledger.RecapVerified);
    Assert.Null(ledger.BalanceHours(Now));
    Assert.Null(ledger.DrivingShortfallMinutes);
    Assert.Null(ledger.Snapshot(Now));
    Assert.Null(ledger.NextUsableRecap(Now));
  }

  [Fact]
  public void CrossingTheBorderDoesNotReuseAnAnchoredJurisdictionsBalance()
  {
    var (ledger, _) = Ledger(33);

    ledger.Enter("CA");
    ledger.Enter("US");

    Assert.False(ledger.Verified);
    Assert.False(ledger.RecapVerified);
    Assert.Null(ledger.BalanceMinutes(Now));
    Assert.Null(ledger.Snapshot(Now));
    Assert.Null(ledger.NextUsableRecap(Now));
  }

  private static (HosCycleFeasibility Ledger, HosHistory History) Ledger(
    int offsetMinutes
  )
  {
    var history = HosForecastFixture.History(
      Now,
      cycleHours: 10,
      firstDayHours: 3
    );
    return (
      new(Now, OffsetClocks(history, offsetMinutes), "US", history),
      history
    );
  }

  private static DriverHosClocks OffsetClocks(
    HosHistory history,
    int offsetMinutes
  )
  {
    var clocks = HosForecastFixture.Clocks(Now, history);
    clocks.CycleMs += offsetMinutes * 60_000L;
    return clocks;
  }

  private static void AssertUnknownRecap(
    HosCycleFeasibility ledger,
    DateTimeOffset at,
    int remainingMinutes
  )
  {
    var snapshot = Assert.IsType<StopCycleForecast>(ledger.Snapshot(at));
    Assert.Equal(remainingMinutes, snapshot.RemainingMinutes);
    Assert.False(snapshot.RecapVerified);
    Assert.Null(snapshot.NextRecapAt);
    Assert.Null(snapshot.NextRecapMinutes);
  }
}
