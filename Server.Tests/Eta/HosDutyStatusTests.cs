using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Models;
using Application.Features.Fleet.Models;
using Infrastructure.Integrations.Samsara;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public class HosDutyStatusTests
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

  [Theory]
  [InlineData("US", 7, 36, 34, 1672)]
  [InlineData("CA", 7, 36, 36, 1792)]
  [InlineData("CA", 14, 72, 72, 3952)]
  public void ResetCountdownUsesCurrentCountryAndCanadianCycle(
    string country,
    int days,
    int restart,
    int target,
    int remaining
  )
  {
    var history = History() with
    {
      CanadaCycle = new(days, days == 7 ? 70 : 120, restart),
      Periods =
      [
        new(Now.AddHours(-8), Now.AddMinutes(-368), "driving"),
        new(Now.AddMinutes(-368), Now, "sleeperBerth"),
      ],
    };
    var result = HosDutyStatus.Read(history, Clocks(), Now, country)!;
    Assert.Equal(target, result.CycleResetHours);
    Assert.Equal(remaining, result.CycleResetRemainingMinutes);
    Assert.Equal(country, result.CycleResetCountry);
    Assert.Equal(232, result.TenHourRestRemainingMinutes);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("MX")]
  [InlineData("CA")]
  public void UnknownCountryOrCanadianCycleDoesNotInventAReset(string? country)
  {
    var result = HosDutyStatus.Read(History(), Clocks(), Now, country)!;
    Assert.Null(result.CycleResetHours);
    Assert.Null(result.CycleResetRemainingMinutes);
  }

  [Fact]
  public void IncompleteOrConflictingRestCannotShowResetCountdown()
  {
    var history = History();
    Assert.Null(
      HosDutyStatus
        .Read(history, Clocks("onDuty"), Now, "US")!
        .CycleResetRemainingMinutes
    );
    Assert.Null(
      HosDutyStatus
        .Read(
          history with
          {
            Through = Now.AddMinutes(-4),
          },
          Clocks(),
          Now,
          "US"
        )!
        .CycleResetRemainingMinutes
    );
    Assert.Null(
      HosDutyStatus
        .Read(
          history with
          {
            Periods = history.Periods.Where((_, i) => i != 2).ToArray(),
          },
          Clocks(),
          Now,
          "US"
        )!
        .CycleResetRemainingMinutes
    );
  }

  [Fact]
  public void CompletedResetHasZeroRemainingTime()
  {
    var history = History() with
    {
      Periods =
      [
        new(Now.AddHours(-40), Now.AddHours(-36), "driving"),
        new(Now.AddHours(-36), Now, "sleeperBerth"),
      ],
    };
    Assert.Equal(
      0,
      HosDutyStatus
        .Read(history, Clocks(), Now, "US")!
        .CycleResetRemainingMinutes
    );
  }

  private static HosHistory History() =>
    new(
      Now.AddDays(-16),
      Now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      null,
      [
        new(Now.AddHours(-8), Now.AddHours(-5), "driving"),
        new(Now.AddHours(-5), Now.AddHours(-4), "offDuty"),
        new(Now.AddHours(-4), Now.AddHours(-3.5), "personalConveyance"),
        new(Now.AddHours(-3.5), Now, "sleeperBerth"),
      ]
    );

  private static DriverHosClocks Clocks(string status = "sleeperBerth") =>
    new()
    {
      CurrentDutyStatus = status,
      UpdatedAt = Now.UtcDateTime,
      DriveMs = 0,
      ShiftMs = 0,
      CycleMs = 54 * 3600000L,
      BreakMs = 8 * 3600000L,
    };

  [Fact]
  public void MixedOffDutyPcAndSleeperKeepOneRestBlockWithoutRequiringFullCycleHistory()
  {
    var history = History();
    Assert.Null(HosTimeline.Create(history, Now));
    var result = HosDutyStatus.Read(history, Clocks(), Now)!;
    Assert.Equal(210, result.StatusMinutes);
    Assert.Equal(300, result.RestMinutes);
    Assert.Equal(300, result.TenHourRestRemainingMinutes);
  }

  [Theory]
  [InlineData("onDuty")]
  [InlineData("yardMove")]
  [InlineData("driving")]
  public void WorkInterruptsTheRestBlock(string status)
  {
    var h = History();
    h = h with
    {
      Periods = h
        .Periods.Select((p, i) => i == 2 ? p with { Status = status } : p)
        .ToArray(),
    };
    var result = HosDutyStatus.Read(h, Clocks(), Now)!;
    Assert.Equal(210, result.RestMinutes);
    Assert.Equal(390, result.TenHourRestRemainingMinutes);
  }

  [Fact]
  public void GapsStaleHistoryAndConflictingCurrentStatusDoNotInventRestCredit()
  {
    var h = History();
    Assert.Null(
      HosDutyStatus
        .Read(
          h with
          {
            Periods = h.Periods.Where((_, i) => i != 2).ToArray(),
          },
          Clocks(),
          Now
        )!
        .RestMinutes
    );
    Assert.Null(
      HosDutyStatus
        .Read(h with { Through = Now.AddMinutes(-4) }, Clocks(), Now)!
        .RestMinutes
    );
    var current = HosDutyStatus.Read(h, Clocks("onDuty"), Now)!;
    Assert.Equal("onDuty", current.Status);
    Assert.Null(current.RestMinutes);
  }

  [Fact]
  public void SamsaraSleeperBedIsNormalizedAndPcCountsAsRest()
  {
    Assert.Equal(
      "sleeperBerth",
      SamsaraHosHistoryProvider.NormalizeStatus("sleeperBed")
    );
    Assert.True(
      new HosPeriod(Now.AddHours(-1), Now, "personalConveyance").Rest
    );
  }

  [Fact]
  public void OngoingRestFinishesBeforeDrivingEvenWhenSomeDriveTimeRemains()
  {
    var clocks = Clocks();
    clocks.DriveMs = 4 * 3600000L;
    clocks.ShiftMs = 6 * 3600000L;
    var clock = new HosTravelClock(Now, clocks, "US");
    clock.CompleteOngoingDailyRest(HosDutyStatus.Read(History(), clocks, Now));
    clock.Drive(2, "US");
    Assert.Equal(Now.AddHours(7), clock.Now);
    Assert.Equal(5, clock.RestHours);
    Assert.True(clock.CompletedOngoingRest);
  }

  [Fact]
  public void TenHourRestDoesNotAwardCycleHours()
  {
    var clocks = Clocks();
    clocks.CycleMs = 3600000L;
    var clock = new HosTravelClock(Now, clocks, "US");
    clock.CompleteOngoingDailyRest(HosDutyStatus.Read(History(), clocks, Now));
    Assert.Equal(60, clock.SnapshotCycle().RemainingMinutes);
    clock.Drive(2, "US");
    Assert.Equal(5, clock.RestHours);
    Assert.Equal(0, clock.SnapshotCycle().RemainingMinutes);
    Assert.Null(clock.CycleResumeAt);
  }

  [Fact]
  public void ShortRestIsNotAssumedToBeAFullDailyRest()
  {
    var clock = new HosTravelClock(
      Now,
      new()
      {
        DriveMs = 5 * 3600000L,
        ShiftMs = 8 * 3600000L,
        CycleMs = 54 * 3600000L,
        BreakMs = 8 * 3600000L,
      },
      "US"
    );
    clock.CompleteOngoingDailyRest(
      new("offDuty", Now.AddHours(-1), Now.AddHours(-1), Now)
    );
    clock.Drive(1, "US");
    Assert.Equal(0, clock.RestHours);
  }
}
