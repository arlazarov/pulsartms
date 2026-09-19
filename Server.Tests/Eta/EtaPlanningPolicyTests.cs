using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Models;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public class EtaPlanningPolicyTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    6,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  private static DriverHosClocks Clocks(double drive = 11, double shift = 14) =>
    new()
    {
      DriveMs = (long)(drive * 3600000),
      ShiftMs = (long)(shift * 3600000),
      CycleMs = 70 * 3600000L,
      BreakMs = 8 * 3600000L,
    };

  [Fact]
  public void TenHourPlanningLimitAddsRestAndPtiBeforeTheNextShift()
  {
    var clock = new HosTravelClock(
      Now,
      Clocks(),
      "US",
      planning: new() { DrivingHoursPerShift = 10 }
    );
    clock.Drive(11, "US");
    clock.CompletePlannedBreak();
    Assert.Equal(Now.AddHours(22.5).AddMinutes(10), clock.Now);
    Assert.Equal(.5, clock.PreTripHours);
    Assert.Equal(11, clock.RestHours);
  }

  [Fact]
  public void FuelDoesNotReplaceTheSeparatePlannedBreak()
  {
    var clock = new HosTravelClock(Now, Clocks(), "US", planning: new());
    clock.Drive(7, "US");
    clock.Fuel();
    clock.Drive(3, "US");
    Assert.Equal(Now.AddHours(10).AddMinutes(50), clock.Now);
    Assert.Equal(5 / 60d, clock.FuelHours);
    Assert.Equal(.5, clock.RestHours);
    Assert.Equal(.25, clock.PreTripHours);
  }

  [Fact]
  public void ExistingShiftUsesOnlyTheRemainingPartOfTenHours()
  {
    var clock = new HosTravelClock(
      Now,
      Clocks(drive: 2, shift: 5),
      "US",
      planning: new() { DrivingHoursPerShift = 10 }
    );
    clock.Drive(2, "US");
    Assert.Equal(.25, clock.PreTripHours);
    Assert.True(clock.RestHours >= 10);
  }

  [Fact]
  public void EndingAShortTripStillIncludesOneBreakWithoutDoubleCounting()
  {
    var clock = new HosTravelClock(Now, Clocks(), "US", planning: new());
    clock.Drive(2, "US");
    clock.CompletePlannedBreak();
    clock.CompletePlannedBreak();
    Assert.Equal(Now.AddHours(2).AddMinutes(50), clock.Now);
    Assert.Equal(.5, clock.RestHours);
  }

  [Fact]
  public void SlowerRoadTimesAreNeverReplacedByThePlanningSpeedCap()
  {
    var policy = new EtaPlanningOptions();
    Assert.Equal(0, policy.TravelTimeBufferPercent);
    Assert.Equal(2, policy.TravelHours(100, 7200), 8);
    Assert.Equal(2, policy.TravelHours(120, 3600), 8);
    Assert.True(policy.TravelHours(100, 10800) > policy.TravelHours(100, 7200));
  }

  [Fact]
  public void DefaultAllowsElevenHoursButNotBeyondEldAvailability()
  {
    Assert.Equal(11, new EtaPlanningOptions().DrivingHoursPerShift);
    Assert.Equal(5, new EtaPlanningOptions().FuelStopMinutes);
    var clock = new HosTravelClock(Now, Clocks(), "US", planning: new());
    clock.Drive(11, "US");
    Assert.Equal(Now.AddHours(11).AddMinutes(50), clock.Now);
    Assert.Equal(.5, clock.RestHours);
    var limited = new HosTravelClock(
      Now,
      Clocks(drive: 1),
      "US",
      planning: new()
    );
    limited.Drive(2, "US");
    Assert.True(limited.RestHours >= 10);
  }
}
