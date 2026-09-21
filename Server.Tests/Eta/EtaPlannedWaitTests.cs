using Domain.Models.Fleet;
using Domain.Rules.Eta;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaPlannedWaitTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    8,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public void TwelveHourFutureWaitRestoresDailyHoursWithoutAnotherDailyRest()
  {
    var clock = new HosTravelClock(
      Now,
      Clocks(cycle: 20),
      "US",
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(12));
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(13).AddMinutes(20), clock.Now);
    Assert.Equal(12, clock.RestHours);
    Assert.Equal(12, clock.PlannedOffDutyWaitHours);
  }

  [Fact]
  public void DailyRestDoesNotRestoreCycleAndCountsOnceTowardALaterRestart()
  {
    var clock = HosForecastFixture.Clock(
      Now,
      HosCycleMode.Restart,
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(12));
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(35).AddMinutes(20), clock.Now);
    Assert.Equal(34, clock.RestHours);
  }

  [Fact]
  public void ThirtySixHourFutureWaitRestoresTheCycleWithoutASecondRestart()
  {
    var clock = HosForecastFixture.Clock(
      Now,
      HosCycleMode.Restart,
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(36));
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(37).AddMinutes(20), clock.Now);
    Assert.Equal(36, clock.RestHours);
  }

  [Fact]
  public void FacilityWorkInterruptsPlannedRestBeforeAnUnfinishedCycleRestart()
  {
    var clock = HosForecastFixture.Clock(
      Now,
      HosCycleMode.Restart,
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(12));
    clock.Service(2);
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(49).AddMinutes(20), clock.Now);
    Assert.Equal(46, clock.RestHours);
  }

  [Fact]
  public void CurrentFacilityWaitingDoesNotSpendCycleOrInventVerifiedCycleHours()
  {
    var clock = new HosTravelClock(
      Now,
      Clocks(cycle: 0),
      "US",
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(36));
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(37).AddMinutes(20), clock.Now);
    Assert.Equal(36, clock.PlannedOffDutyWaitHours);
    Assert.Equal(36, clock.RestHours);
    Assert.False(clock.CycleFeasibility.Verified);
    Assert.Equal(0, clock.SnapshotCycle().RemainingMinutes);
  }

  [Fact]
  public void UnknownCanadianCycleDoesNotInventARestart()
  {
    var clock = new HosTravelClock(
      Now,
      Clocks(cycle: 0),
      "CA",
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(36));
    clock.Drive(1, "CA");
    Assert.Equal(Now.AddHours(37).AddMinutes(20), clock.Now);
    Assert.Equal(36, clock.RestHours);
    Assert.False(clock.CycleFeasibility.Verified);
    Assert.Null(clock.CycleResumeAt);
  }

  private static DriverHosClocks Clocks(double cycle) =>
    new()
    {
      DriveMs = 0,
      ShiftMs = 0,
      CycleMs = (long)(cycle * 3600000),
      BreakMs = 0,
      UpdatedAt = Now.UtcDateTime,
    };
}
