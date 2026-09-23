using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

// Which stops are this shift's: those reached before the driver's current
// work period ends, plus a two-hour selection buffer. The line is read from
// the driver's hours, not the calendar.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelIssueHorizonTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    23,
    14,
    0,
    0,
    TimeSpan.Zero
  );
  private static readonly TimeSpan Buffer = TimeSpan.FromHours(2);
  private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

  [Fact]
  public void OnDutyTheShiftEndsWithTheWindowPlusTheBuffer()
  {
    var plan = Plan(3, 9, 11, 12);
    FuelIssueHorizon.Apply(
      plan,
      Clocks("driving", shiftHours: 9),
      Now,
      Buffer,
      Fresh
    );

    Assert.Equal(FuelIssueStates.Ready, plan.IssueState);
    Assert.Equal(Now.AddHours(11), plan.IssueHorizonEndsAt);
    Assert.Equal(
      new[] { "current", "current", "current", "upcoming" },
      plan.Stops.Select(x => x.IssueHorizon)
    );
  }

  [Fact]
  public void TheBufferIsInclusiveAndAMinuteLaterIsNot()
  {
    var plan = Plan(11, 11.0 + 1d / 60);
    FuelIssueHorizon.Apply(
      plan,
      Clocks("onDuty", shiftHours: 9),
      Now,
      Buffer,
      Fresh
    );
    Assert.Equal(
      new[] { "current", "upcoming" },
      plan.Stops.Select(x => x.IssueHorizon)
    );
  }

  // A shift that starts in the evening runs past midnight; the calendar
  // day has nothing to do with where it ends.
  [Fact]
  public void AnOvernightShiftIsNotCutAtMidnight()
  {
    var evening = new DateTimeOffset(2026, 9, 23, 22, 0, 0, TimeSpan.Zero);
    var plan = Plan(4, 9);
    FuelIssueHorizon.Apply(
      plan,
      Clocks("driving", shiftHours: 8, at: evening),
      evening,
      Buffer,
      Fresh
    );
    Assert.Equal("current", plan.Stops[0].IssueHorizon);
    Assert.Equal(evening.AddHours(10), plan.IssueHorizonEndsAt);
    Assert.Equal("current", plan.Stops[1].IssueHorizon);
  }

  [Theory]
  [InlineData("offDuty")]
  [InlineData("sleeperBerth")]
  [InlineData("personalConveyance")]
  public void RestIsPreparedButHeldUntilDuty(string duty)
  {
    var plan = Plan(1, 20);
    FuelIssueHorizon.Apply(
      plan,
      Clocks(duty, shiftHours: 9),
      Now,
      Buffer,
      Fresh
    );
    Assert.Equal(FuelIssueStates.AwaitingDuty, plan.IssueState);
    Assert.Equal("current", plan.Stops[0].IssueHorizon);
  }

  [Fact]
  public void StaleOrMissingHoursNeverMakeAShift()
  {
    var stale = Plan(1, 2);
    FuelIssueHorizon.Apply(
      stale,
      Clocks("driving", shiftHours: 9, at: Now.AddMinutes(-11)),
      Now,
      Buffer,
      Fresh
    );
    var missing = Plan(1, 2);
    FuelIssueHorizon.Apply(missing, null, Now, Buffer, Fresh);

    foreach (var plan in new[] { stale, missing })
    {
      Assert.Equal(FuelIssueStates.HosUnknown, plan.IssueState);
      Assert.Null(plan.IssueHorizonEndsAt);
      Assert.All(plan.Stops, x => Assert.Equal("upcoming", x.IssueHorizon));
    }
  }

  [Fact]
  public void WithoutAWindowDrivingTimeIsTheShorterBound()
  {
    var plan = Plan(6, 8);
    var clocks = Clocks("driving", shiftHours: null);
    clocks.DriveMs = (long)TimeSpan.FromHours(5).TotalMilliseconds;
    FuelIssueHorizon.Apply(plan, clocks, Now, Buffer, Fresh);
    Assert.Equal(Now.AddHours(7), plan.IssueHorizonEndsAt);
    Assert.Equal(
      new[] { "current", "upcoming" },
      plan.Stops.Select(x => x.IssueHorizon)
    );
  }

  [Fact]
  public void AStopWithNoArrivalEndsTheShiftThere()
  {
    var plan = Plan(1, 2, 3);
    plan.Stops[1].EstimatedArrival = null;
    FuelIssueHorizon.Apply(
      plan,
      Clocks("driving", shiftHours: 9),
      Now,
      Buffer,
      Fresh
    );
    Assert.Equal(
      new[] { "current", "upcoming", "upcoming" },
      plan.Stops.Select(x => x.IssueHorizon)
    );
  }

  [Fact]
  public void AStopTheTankCannotReachIsCriticalAtOnce()
  {
    var plan = Plan(1, 2);
    FuelIssueHorizon.Apply(
      plan,
      Clocks("driving", shiftHours: 9),
      Now,
      Buffer,
      Fresh
    );
    Assert.False(plan.IssueCritical);
    plan.Stops[1].ArrivalGallons = -3;
    FuelIssueHorizon.Apply(plan, null, Now, Buffer, Fresh);
    Assert.True(plan.IssueCritical);
  }

  private static DriverHosClocks Clocks(
    string duty,
    double? shiftHours,
    DateTimeOffset? at = null
  ) =>
    new()
    {
      CurrentDutyStatus = duty,
      ShiftMs = shiftHours is { } hours
        ? (long)TimeSpan.FromHours(hours).TotalMilliseconds
        : null,
      UpdatedAt = (at ?? Now).UtcDateTime,
    };

  private static FuelPlan Plan(params double[] hoursAhead) =>
    new()
    {
      Stops = hoursAhead
        .Select(hours => new FuelPlanStop
        {
          StationId = Guid.NewGuid(),
          EstimatedArrival = Now.AddHours(hours),
          ArrivalGallons = 60,
        })
        .ToList(),
    };
}
