using Application.Features.Eta.Algorithms;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaSleeperPlanningTests
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
  public void FacilityServiceAdvancesDepartureWithoutSpendingCycle()
  {
    var history = HosForecastFixture.History(Now, cycleHours: 20);
    var clock = new HosTravelClock(
      Now,
      HosForecastFixture.Clocks(Now, history),
      "US",
      history,
      planning: new()
    );
    clock.Drive(1, "US");
    var arrival = clock.Now;
    var balance = clock.CycleFeasibility.BalanceMinutes(arrival);
    clock.StopRest(2);
    Assert.Equal(arrival.AddHours(2), clock.Now);
    Assert.Equal(balance, clock.CycleFeasibility.BalanceMinutes(clock.Now));
    Assert.Null(clock.CycleFeasibility.FirstShortageAt);
  }

  [Fact]
  public void DrivingDaySpendsElevenHoursDrivingAndTwentyMinutesOverheadDespiteMultipleStops()
  {
    var history = HosForecastFixture.History(Now, cycleHours: 40);
    var clock = new HosTravelClock(
      Now,
      HosForecastFixture.Clocks(Now, history),
      "US",
      history,
      planning: new()
    );
    clock.Drive(4, "US");
    clock.StopRest(1);
    clock.Drive(3, "US");
    clock.StopRest(1);
    clock.Fuel();
    clock.Drive(4, "US");
    Assert.Equal(15 / 60d, clock.PreTripHours);
    Assert.Equal(5 / 60d, clock.FuelHours);
    Assert.Equal(
      40 * 60 - 11 * 60 - 20,
      clock.CycleFeasibility.BalanceMinutes(clock.Now)
    );
    Assert.Equal(Now.AddHours(13).AddMinutes(50), clock.Now);
  }

  [Fact]
  public void SleeperServiceCreditsRecapAtMidnightWithoutChargingWork()
  {
    var now = Now.AddHours(11);
    var history = HosForecastFixture.History(
      now,
      cycleHours: 1,
      firstDayHours: 185d / 60
    );
    var clock = new HosTravelClock(
      now,
      HosForecastFixture.Clocks(now, history),
      "US",
      history,
      planning: new()
    );
    clock.StopRest(2);
    Assert.Equal(60 + 185, clock.CycleFeasibility.BalanceMinutes(clock.Now));
    Assert.Equal(0, clock.FuelHours);
    Assert.Equal(0, clock.PreTripHours);
    Assert.Null(clock.CycleResumeAt);
  }

  [Fact]
  public void AppointmentWaitAndServiceFormOneRestWithoutAddingAnotherTenHours()
  {
    var history = HosForecastFixture.History(Now, cycleHours: 20);
    var clock = new HosTravelClock(
      Now,
      HosForecastFixture.Clocks(Now, history, drive: 0, shift: 0),
      "US",
      history,
      planning: new()
    );
    clock.WaitUntil(Now.AddHours(8));
    clock.StopRest(2);
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(11).AddMinutes(20), clock.Now);
    Assert.Equal(10, clock.RestHours);
    Assert.Equal(15 / 60d, clock.PreTripHours);
    Assert.Equal(5 / 60d, clock.FuelHours);
  }
}
