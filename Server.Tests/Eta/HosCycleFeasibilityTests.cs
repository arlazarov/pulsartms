using Application.Features.Eta.Algorithms;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class HosCycleFeasibilityTests
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
  public void HistoricalRestartEndingAtTheAnchorDoesNotOverrideCurrentEldHours()
  {
    var history = HosForecastFixture.History(Now) with
    {
      From = Now.AddDays(-16),
      Periods = [new(Now.AddDays(-16), Now, "offDuty")],
    };
    var clocks = HosForecastFixture.Clocks(Now, history);
    clocks.CycleMs -= 10 * 60_000;
    var ledger = new HosCycleFeasibility(Now, clocks, "US", history);

    Assert.True(ledger.Verified);
    Assert.True(ledger.RecapVerified);
    Assert.Equal(4190, ledger.BalanceMinutes(Now));
    ledger.Observe(Now, Now.AddHours(1), "onDuty");
    Assert.Equal(4130, ledger.BalanceMinutes(Now.AddHours(1)));
    Assert.Equal(
      4200,
      ledger.BalanceMinutes(
        new DateTimeOffset(Now.Date.AddDays(8), TimeSpan.Zero)
      )
    );
  }

  [Fact]
  public void RecapBeforeDepletionDoesNotCreateDrivingShortage()
  {
    var history = HosForecastFixture.History(
      Now,
      cycleHours: 1,
      firstDayHours: 185d / 60
    );
    var ledger = new HosCycleFeasibility(
      Now,
      HosForecastFixture.Clocks(Now, history),
      "US",
      history
    );
    ledger.Observe(Now, Now.AddHours(2), "driving");
    Assert.True(ledger.Verified);
    Assert.Equal(125, ledger.BalanceMinutes(Now.AddHours(2)));
    Assert.Equal(0, ledger.DrivingShortfallMinutes);
    Assert.Null(ledger.FirstShortageAt);
  }

  [Fact]
  public void LaterPositiveRecapDoesNotEraseEarlierBlockedDriving()
  {
    var history = HosForecastFixture.History(
      Now,
      cycleHours: .5,
      firstDayHours: 185d / 60
    );
    var ledger = new HosCycleFeasibility(
      Now,
      HosForecastFixture.Clocks(Now, history),
      "US",
      history
    );
    ledger.Observe(Now, Now.AddHours(2), "driving");
    Assert.Equal(95, ledger.BalanceMinutes(Now.AddHours(2)));
    Assert.Equal(30, ledger.DrivingShortfallMinutes);
    Assert.Equal(Now.AddMinutes(30), ledger.FirstShortageAt);
  }

  [Fact]
  public void NonDrivingWorkMayBeNegativeWithoutInventingEarlierDrivingViolation()
  {
    var history = HosForecastFixture.History(Now, cycleHours: 1);
    var ledger = new HosCycleFeasibility(
      Now,
      HosForecastFixture.Clocks(Now, history),
      "US",
      history
    );
    ledger.Observe(Now, Now.AddHours(2), "onDuty");
    Assert.Equal(-60, ledger.BalanceMinutes(Now.AddHours(2)));
    Assert.Equal(0, ledger.DrivingShortfallMinutes);
    Assert.Null(ledger.FirstShortageAt);
    ledger.Observe(Now.AddHours(2), Now.AddHours(2.5), "driving");
    Assert.Equal(90, ledger.DrivingShortfallMinutes);
    Assert.Equal(Now.AddHours(2), ledger.FirstShortageAt);
  }

  [Fact]
  public void RecapWaitCreditsDailyRestEvenWhenOldShiftStillHasHours()
  {
    var now = new DateTimeOffset(Now.Date.AddHours(13), TimeSpan.Zero);
    var history = HosForecastFixture.History(now, firstDayHours: 10);
    var clock = new HosTravelClock(
      now,
      HosForecastFixture.Clocks(now, history, shift: 13),
      "US",
      history,
      planning: new(),
      cycleMode: HosCycleMode.Recap
    );
    clock.Drive(3, "US");
    Assert.Equal(now.AddHours(14).AddMinutes(20), clock.Now);
    Assert.Equal(11, clock.RestHours);
    Assert.Equal(1, clock.RecapWaits);
    Assert.Equal(0, clock.CycleFeasibility.DrivingShortfallMinutes);
  }

  [Fact]
  public void RecapScenarioSkipsAnEmptyBoundaryWithoutAssumingRestart()
  {
    var clock = HosForecastFixture.Clock(
      Now,
      HosCycleMode.Recap,
      planning: new()
    );
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(26).AddMinutes(20), clock.Now);
    Assert.Equal(25, clock.RestHours);
    Assert.Equal(Now.AddHours(25), clock.CycleResumeAt);
    Assert.Null(clock.CycleFeasibility.FirstShortageAt);
    Assert.InRange(
      clock.CycleFeasibility.BalanceMinutes(clock.Now)!.Value,
      519,
      520
    );
  }

  [Fact]
  public void ExplicitRestartCreditsExistingRestOnce()
  {
    var clock = HosForecastFixture.Clock(
      Now,
      HosCycleMode.Restart,
      ongoingRestHours: 8,
      planning: new()
    );
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(27).AddMinutes(20), clock.Now);
    Assert.Equal(26, clock.RestHours);
    Assert.Equal(Now.AddHours(-8), clock.CycleRestStartedAt);
    Assert.Equal(Now.AddHours(26), clock.CycleResumeAt);
    Assert.InRange(
      clock.CycleFeasibility.BalanceMinutes(clock.Now)!.Value,
      4119,
      4120
    );
  }

  [Fact]
  public void LongAppointmentGapDoesNotSilentlyRestartBaselineCycle()
  {
    var clock = HosForecastFixture.Clock(Now, HosCycleMode.Observe);
    clock.WaitUntil(Now.AddHours(36));
    Assert.True(clock.CycleFeasibility.Verified);
    Assert.Equal(600, clock.CycleFeasibility.BalanceMinutes(clock.Now));
    Assert.Null(clock.CycleResumeAt);
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(37), clock.Now);
  }

  [Fact]
  public void UnknownAndCrossBorderHistoryDoNotInventSignedHours()
  {
    var history = HosForecastFixture.History(Now);
    var clocks = HosForecastFixture.Clocks(Now, history);
    var missing = new HosCycleFeasibility(Now, clocks, "US", null);
    Assert.False(missing.Verified);
    Assert.Null(missing.BalanceMinutes(Now));
    Assert.Null(missing.DrivingShortfallMinutes);
    var border = new HosCycleFeasibility(Now, clocks, "US", history);
    border.Enter("CA");
    border.Enter("US");
    Assert.False(border.Verified);
    Assert.Null(border.BalanceMinutes(Now));
  }
}
