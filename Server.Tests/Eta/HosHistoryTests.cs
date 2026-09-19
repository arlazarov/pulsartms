using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Models;
using Application.Features.Fleet.Models;
using Infrastructure.Integrations.Samsara;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public class HosHistoryTests
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

  private static HosHistory SplitHistory(string status = "offDuty") =>
    new(
      Now.AddHours(-24),
      Now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      new(7, 70, 36),
      [
        new(Now.AddHours(-24), Now.AddHours(-14), "sleeperBerth"),
        new(Now.AddHours(-14), Now.AddHours(-10), "driving"),
        new(Now.AddHours(-10), Now.AddHours(-7), status),
        new(Now.AddHours(-7), Now, "driving"),
      ]
    );

  [Fact]
  public void ThreePlusSevenRestDoesNotGiveFreshElevenHours()
  {
    var split = HosTimeline.Create(SplitHistory(), Now)!.FindSplit(Now, "US")!;
    Assert.Equal(7, split.RestHours);
    Assert.Equal(4, split.DriveLeft);
    Assert.Equal(7, split.ShiftLeft);
  }

  [Fact]
  public void SplitForecastFinishesQualifyingRestInsteadOfTenHours()
  {
    var clocks = new DriverHosClocks
    {
      DriveMs = 0,
      ShiftMs = 0,
      CycleMs = 50 * 3600000L,
      BreakMs = 3600000,
    };
    var clock = new HosTravelClock(Now, clocks, "US", SplitHistory());
    clock.Drive(1, "US");
    Assert.Equal(Now.AddHours(8), clock.Now);
    Assert.Equal(1, clock.SplitRests);
  }

  [Fact]
  public void CanadaDoesNotAcceptShortOffDutyAsSleeper()
  {
    Assert.Null(HosTimeline.Create(SplitHistory(), Now)!.FindSplit(Now, "CA"));
    Assert.NotNull(
      HosTimeline
        .Create(SplitHistory("sleeperBerth"), Now)!
        .FindSplit(Now, "CA")
    );
  }

  [Fact]
  public void MissingAndConflictingIntervalsDisableCredits()
  {
    var h = SplitHistory();
    Assert.Null(
      HosTimeline.Create(h with { Periods = h.Periods.Skip(1).ToArray() }, Now)
    );
    Assert.Null(
      HosTimeline.Create(
        h with
        {
          Periods = h
            .Periods.Append(new(Now.AddHours(-3), Now, "offDuty"))
            .ToArray(),
        },
        Now
      )
    );
  }

  [Fact]
  public void SevenOffDutyCannotBeLongSleeperPortion()
  {
    var h = new HosHistory(
      Now.AddHours(-28),
      Now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      null,
      [
        new(Now.AddHours(-28), Now.AddHours(-18), "offDuty"),
        new(Now.AddHours(-18), Now.AddHours(-13), "driving"),
        new(Now.AddHours(-13), Now.AddHours(-6), "offDuty"),
        new(Now.AddHours(-6), Now, "driving"),
      ]
    );
    Assert.Equal(
      7,
      HosTimeline.Create(h, Now)!.FindSplit(Now, "US")!.RestHours
    );
    var actual = h with
    {
      Periods = h
        .Periods.Select(
          (p, i) => i == 2 ? p with { Status = "sleeperBerth" } : p
        )
        .ToArray(),
    };
    Assert.Equal(
      3,
      HosTimeline.Create(actual, Now)!.FindSplit(Now, "US")!.RestHours
    );
  }

  [Fact]
  public void RecapWaitsForHomeMidnightAndKeepsDailyRest()
  {
    var now = new DateTimeOffset(2026, 9, 6, 23, 0, 0, TimeSpan.Zero);
    var start = now.Date.AddDays(-16);
    var logs = new List<HosPeriod>();
    DateTimeOffset cursor = start;
    for (var d = now.Date.AddDays(-7); d <= now.Date; d = d.AddDays(1))
    {
      var on = new DateTimeOffset(d.AddHours(9), TimeSpan.Zero);
      var end = on.AddHours(d == now.Date ? 7 : 9);
      logs.Add(new(cursor, on, "offDuty"));
      logs.Add(new(on, end, "onDuty"));
      cursor = end;
    }
    logs.Add(new(cursor, now, "offDuty"));
    var history = new HosHistory(
      start,
      now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      null,
      logs
    );
    var clock = new HosTravelClock(
      now,
      new()
      {
        DriveMs = 11 * 3600000L,
        ShiftMs = 0,
        CycleMs = 0,
        BreakMs = 8 * 3600000L,
      },
      "US",
      history,
      cycleMode: HosCycleMode.Recap
    );
    Assert.True(clock.RecapVerified);
    clock.Drive(1, "US");
    Assert.Equal(1, clock.RecapWaits);
    Assert.Equal(now.AddHours(4), clock.Now);
  }

  [Fact]
  public void DayBoundaryUsesHomeTimezoneAcrossDst()
  {
    var h = SplitHistory() with { TimeZoneId = "America/New_York" };
    var t = HosTimeline.Create(h, Now)!;
    Assert.Equal(
      new DateTimeOffset(2026, 9, 7, 4, 0, 0, TimeSpan.Zero),
      t.NextDay(Now)
    );
    Assert.Equal(
      new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero),
      t.NextDay(new(2026, 11, 1, 4, 0, 0, TimeSpan.Zero))
    );
  }

  [Theory]
  [InlineData("USA 70 hour / 8 day", "US", 8, 70)]
  [InlineData("Canada South Cycle 2 (120 hour / 14 day)", "CA", 14, 120)]
  public void UsesConfiguredCycle(
    string text,
    string country,
    int days,
    int hours
  )
  {
    var rule = SamsaraHosHistoryProvider.ParseRule(text, country)!;
    Assert.Equal(days, rule.Days);
    Assert.Equal(hours, rule.Hours);
    Assert.Null(SamsaraHosHistoryProvider.ParseRule("unknown", country));
  }
}
