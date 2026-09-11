using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Models;
using Application.Features.Fleet.Models;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public class HosCycleForecastTests
{
  private static readonly DateTimeOffset Now = new(2026, 9, 8, 23, 0, 0, TimeSpan.Zero);

  [Fact]
  public void ServiceConsumesCycleAndMissingHistoryDoesNotInventRecap()
  {
    var clock = new HosTravelClock(Now, Clocks(2 - 1d / 3600), "US");
    Assert.Equal(119, clock.SnapshotCycle().RemainingMinutes);

    clock.Service(3);
    var result = clock.SnapshotCycle();

    Assert.Equal(0, result.RemainingMinutes);
    AssertUnknownRecap(result);
  }

  [Fact]
  public void ServiceCrossingHomeDayIncludesReturningHoursWithoutAdvancingTheClock()
  {
    var history = History(Now, day => day == new DateTime(2026, 9, 1) ? 8 : 1);
    var clock = Clock(Now, history);
    clock.Service(2);
    var afterService = clock.Now;

    var result = clock.SnapshotCycle();

    Assert.Equal(61 * 60, result.RemainingMinutes);
    Assert.Equal(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), result.NextRecapAt);
    Assert.Equal(60, result.NextRecapMinutes);
    Assert.Equal(afterService, clock.Now);
    Assert.Equal(0, clock.RestHours);
    Assert.Equal(result, clock.SnapshotCycle());
  }

  [Theory]
  [InlineData("2026-09-08T23:00:00Z", 0, "2026-09-09T00:00:00-04:00")]
  [InlineData("2026-09-08T23:00:00Z", 4, "2026-09-09T04:00:00-04:00")]
  [InlineData("2026-11-01T04:30:00Z", 1, "2026-11-01T01:00:00-05:00")]
  [InlineData("2026-11-01T05:30:00Z", 1, "2026-11-01T01:00:00-05:00")]
  [InlineData("2026-03-08T05:30:00Z", 2, "2026-03-08T03:00:00-04:00")]
  public void RecapUsesDriverHomeDayIncludingDst(string nowText, int dayStart, string recapText)
  {
    var now = DateTimeOffset.Parse(nowText);
    var history = History(now) with { TimeZoneId = "America/New_York", DayStartHour = dayStart };

    var result = Clock(now, history).SnapshotCycle();

    Assert.True(result.RecapVerified);
    Assert.Equal(62 * 60, result.RemainingMinutes);
    var expected = DateTimeOffset.Parse(recapText);
    Assert.Equal(expected, result.NextRecapAt);
    Assert.Equal(expected.Offset, result.NextRecapAt!.Value.Offset);
    Assert.Equal(60, result.NextRecapMinutes);
    Assert.Equal("America/New_York", result.HomeTimeZoneId);
  }

  [Theory]
  [InlineData(7, 60, 52, 120)]
  [InlineData(8, 70, 61, 60)]
  public void ConfiguredCycleDeterminesWhichDaysDutyReturns(int days, int hours, int remaining, int returning)
  {
    var history = History(Now, day => day == new DateTime(2026, 9, 2) ? 2 : 1)
      with { UsCycle = new(days, hours, 34) };

    var result = Clock(Now, history).SnapshotCycle();

    Assert.Equal(remaining * 60, result.RemainingMinutes);
    Assert.Equal(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero), result.NextRecapAt);
    Assert.Equal(returning, result.NextRecapMinutes);
  }

  [Fact]
  public void NextRecapSkipsAHomeDayWithNoReturningDuty()
  {
    var history = History(Now, day => day == new DateTime(2026, 9, 1) ? 0 : 1);

    var result = Clock(Now, history).SnapshotCycle();

    Assert.Equal(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), result.NextRecapAt);
    Assert.Equal(60, result.NextRecapMinutes);
  }

  [Fact]
  public void MismatchedAndStaleHistoryKeepRecapUnknown()
  {
    var history = History(Now);

    AssertUnknownRecap(new HosTravelClock(Now, Clocks(1), "US", history).SnapshotCycle());
    AssertUnknownRecap(new HosTravelClock(Now, Clocks(62), "US",
      history with { Through = Now.AddMinutes(-4) }).SnapshotCycle());
  }

  [Fact]
  public void BorderChangeDoesNotReusePreviousJurisdictionsRecap()
  {
    var clock = Clock(Now, History(Now));
    Assert.True(clock.SnapshotCycle().RecapVerified);

    clock.Enter("CA");
    AssertUnknownRecap(clock.SnapshotCycle());
    clock.Enter("US");
    AssertUnknownRecap(clock.SnapshotCycle());
  }

  [Fact]
  public void RestartExcludesEarlierDutyAndLaterPlannedServiceCanReturn()
  {
    var now = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    var clock = Clock(now, History(now), mode: HosCycleMode.Restart);
    clock.WaitUntil(now.AddHours(34));

    var restarted = clock.SnapshotCycle();
    Assert.Equal(70 * 60, restarted.RemainingMinutes);
    Assert.True(restarted.RecapVerified);
    Assert.Null(restarted.NextRecapAt);
    Assert.Null(restarted.NextRecapMinutes);

    clock.Service(2);
    var afterService = clock.SnapshotCycle();
    Assert.Equal(68 * 60, afterService.RemainingMinutes);
    Assert.Equal(new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), afterService.NextRecapAt);
    Assert.Equal(120, afterService.NextRecapMinutes);
  }

  [Fact]
  public void CanadaCycleTwoDoesNotAdvertiseRecapBlockedByDutySinceDailyRest()
  {
    var history = History(Now, day => day == new DateTime(2026, 8, 31) ? 0 : 6);

    var result = Clock(Now, history, "CA").SnapshotCycle();

    Assert.True(result.RecapVerified);
    Assert.Equal(22 * 60, result.RemainingMinutes);
    Assert.Null(result.NextRecapAt);
    Assert.Null(result.NextRecapMinutes);
  }

  [Fact]
  public void ReadingSnapshotsDoesNotChangeFollowingTravelOrRest()
  {
    var history = History(Now, day => day == new DateTime(2026, 9, 1) ? 8 : 1);
    var observed = Clock(Now, history);
    var baseline = Clock(Now, history);
    observed.Service(2);
    baseline.Service(2);
    for (var i = 0; i < 10; i++) observed.SnapshotCycle();

    observed.Drive(18, "US");
    baseline.Drive(18, "US");

    Assert.Equal(baseline.Now, observed.Now);
    Assert.Equal(baseline.DriveHours, observed.DriveHours);
    Assert.Equal(baseline.RestHours, observed.RestHours);
    Assert.Equal(baseline.RecapWaits, observed.RecapWaits);
    Assert.Equal(baseline.SplitRests, observed.SplitRests);
    Assert.Equal(baseline.SnapshotCycle(), observed.SnapshotCycle());
  }

  private static HosTravelClock Clock(DateTimeOffset now, HosHistory history, string country = "US", HosCycleMode mode = HosCycleMode.Observe)
  {
    var rule = history.Rule(country)!;
    var used = HosTimeline.Create(history, now)!.CycleUsed(now, rule);
    return new(now, Clocks(rule.Hours - used), country, history, cycleMode: mode);
  }

  private static DriverHosClocks Clocks(double cycleHours) => new()
  {
    DriveMs = 11 * 3600000L,
    ShiftMs = 14 * 3600000L,
    CycleMs = (long)Math.Round(cycleHours * 3600000),
    BreakMs = 8 * 3600000L
  };

  private static HosHistory History(DateTimeOffset now, Func<DateTime, double>? dutyHours = null)
  {
    var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-16), TimeSpan.Zero);
    var periods = new List<HosPeriod>();
    var cursor = start;
    for (var day = start.Date; day <= now.UtcDateTime.Date; day = day.AddDays(1))
    {
      var on = new DateTimeOffset(day.AddHours(9), TimeSpan.Zero);
      var hours = dutyHours?.Invoke(day) ?? 1;
      if (on >= now || hours == 0) continue;
      var end = on.AddHours(hours);
      if (end > now) end = now;
      if (cursor < on) periods.Add(new(cursor, on, "offDuty"));
      periods.Add(new(on, end, "onDuty"));
      cursor = end;
    }
    if (cursor < now) periods.Add(new(cursor, now, "offDuty"));
    return new(start, now, "Etc/UTC", 0, new(8, 70, 34), new(14, 120, 72), periods);
  }

  private static void AssertUnknownRecap(StopCycleForecast result)
  {
    Assert.False(result.RecapVerified);
    Assert.Null(result.NextRecapAt);
    Assert.Null(result.NextRecapMinutes);
    Assert.Null(result.HomeTimeZoneId);
  }
}
