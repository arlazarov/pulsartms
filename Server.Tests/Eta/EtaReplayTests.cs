using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Eta;
using Domain.Rules.Ports;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaReplayTests
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
  private static DriverHosClocks Clocks =>
    new()
    {
      DriveMs = 9 * 3600000L,
      ShiftMs = 12 * 3600000L,
      CycleMs = 60 * 3600000L,
      BreakMs = 6 * 3600000L,
      UpdatedAt = Now.UtcDateTime,
    };

  [Theory]
  [InlineData(-81, false, "US", 34)]
  [InlineData(-80, false, "CA", 36)]
  [InlineData(-81, true, null, null)]
  public void DutyResetUsesFreshTruckPositionNotTheDestination(
    double longitude,
    bool stale,
    string? country,
    int? hours
  )
  {
    var plan = Plan(120, 7200);
    plan.InputsChanged = true;
    var state = State(plan, 0);
    state = state with
    {
      Progress = state.Progress! with
      {
        Position = new(35, longitude),
        LocationStale = stale,
      },
    };
    var clocks = Clocks;
    clocks.CurrentDutyStatus = "sleeperBerth";
    var history = new HosHistory(
      Now.AddHours(-8),
      Now,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      new(7, 70, 36),
      [
        new(Now.AddHours(-8), Now.AddHours(-6), "driving"),
        new(Now.AddHours(-6), Now, "sleeperBerth"),
      ]
    );
    var result = Service(new CountryRegions())
      .Calculate(state, clocks, Now.UtcDateTime, history);
    Assert.Equal(country, result.DutyStatus!.CycleResetCountry);
    Assert.Equal(hours, result.DutyStatus.CycleResetHours);
    Assert.Equal(
      hours.HasValue ? (hours.Value - 6) * 60 : (int?)null,
      result.DutyStatus.CycleResetRemainingMinutes
    );
  }

  private sealed class CountryRegions : IRouteRegionLookup
  {
    public RouteRegion Find(RoutePoint point) =>
      new(point.Longitude < -80.5 ? "US" : "CA", "Etc/UTC", false);
  }

  [Fact]
  public void SavedFuelStopsDoNotAddRepeatedDailyAllowancesToAnExistingShift()
  {
    var plan = Plan(120, 7200);
    plan.FuelPlan = new()
    {
      Stops = [new() { MilesAhead = 30 }, new() { MilesAhead = 90 }],
    };
    var result = Service(new Regions())
      .Calculate(State(plan, 60), Clocks, Now.UtcDateTime);
    var stop = Assert.Single(result.Stops);
    Assert.Equal(Now.AddMinutes(60), stop.Arrival);
    Assert.Equal(0, stop.FuelMinutes);
    Assert.Equal(60, stop.DrivingMinutes);
  }

  // The allowance buys the minutes a fuel stop costs beyond the driving, so
  // a shift that passes no pump owes nothing for one. A plan that has not
  // been fuelled yet has decided nothing, and keeps its allowance rather
  // than having one quietly taken away.
  [Theory]
  [InlineData(0d, 0)]
  [InlineData(90d, 5)]
  [InlineData(null, 5)]
  public void TheFuelAllowanceIsOwedOncePerStopThePlanMakesToFuel(
    double? milesAhead,
    int fuel
  )
  {
    var plan = Plan(120, 7200);
    if (milesAhead is { } ahead)
      plan.FuelPlan = new() { Stops = [new() { MilesAhead = ahead }] };
    var fresh = Clocks;
    fresh.DriveMs = 11 * 3600000L;
    fresh.ShiftMs = 14 * 3600000L;
    var stop = Assert.Single(
      Service(new Regions())
        .Calculate(State(plan, 60), fresh, Now.UtcDateTime)
        .Stops
    );
    Assert.Equal(fuel, stop.FuelMinutes);
    // The pre-trip belongs to the shift starting, not to the fuelling.
    Assert.Equal(15, stop.PreTripMinutes);
  }

  // 11006, September 25: a fresh shift a few miles from its pickup, a full
  // tank, and the plan's one pump far on, before a stop of the next load.
  // The pre-trip is owed now; the fuel allowance is spent where the road
  // reaches the pump, so the pickup's arrival carries none of it. A pump
  // placed before this stop is still paid on the way to it.
  [Theory]
  [InlineData(false, 0)]
  [InlineData(true, 5)]
  public void APlannedPumpIsPaidWhereTheRoadReachesIt(
    bool beforeThisStop,
    int fuel
  )
  {
    var plan = Plan(3, 240);
    plan.DispatchId = Guid.NewGuid();
    plan.FuelPlan = new()
    {
      Stops =
      [
        new()
        {
          MilesAhead = beforeThisStop ? 2 : 947,
          DispatchId = beforeThisStop ? plan.DispatchId : Guid.NewGuid(),
          BeforeStopId = beforeThisStop ? plan.Stops[0].Id : Guid.NewGuid(),
        },
      ],
    };
    var fresh = Clocks;
    fresh.DriveMs = 11 * 3600000L;
    fresh.ShiftMs = 14 * 3600000L;

    var stop = Assert.Single(
      Service(new Regions())
        .Calculate(State(plan, 0), fresh, Now.UtcDateTime)
        .Stops
    );

    Assert.Equal(fuel, stop.FuelMinutes);
    Assert.Equal(15, stop.PreTripMinutes);
    Assert.Equal(4, stop.DrivingMinutes);
    Assert.Equal(Now.AddMinutes(15 + fuel + 4), stop.Arrival);
  }

  // The owner's personal conveyance approach: moving in personal
  // conveyance within 50 km (road) of the next stop, that arrival excludes
  // the pre-trip, which is owed after it. Parked, stale telemetry, farther
  // than 50 km, or another status: the pre-trip first.
  [Theory]
  [InlineData("personalConveyance", 45d, 31d, false, true)]
  [InlineData("personalConveyance", 45d, 31.1d, false, false)]
  [InlineData("personalConveyance", 0d, 3d, false, false)]
  [InlineData("personalConveyance", 45d, 3d, true, false)]
  [InlineData("offDuty", 45d, 3d, false, false)]
  [InlineData("onDuty", 45d, 3d, false, false)]
  public void MovingInPersonalConveyanceNearTheStopDefersThePreTrip(
    string status,
    double speed,
    double milesAhead,
    bool stale,
    bool deferred
  )
  {
    var plan = Plan(milesAhead, milesAhead * 60);
    var clocks = Fresh(status);
    var state = State(plan, 0);
    state = state with
    {
      Progress = state.Progress! with
      {
        SpeedMph = speed,
        LocationStale = stale,
      },
    };

    var result = Service(new Regions())
      .Calculate(state, clocks, Now.UtcDateTime);

    // Stale telemetry withholds the forecast altogether (readiness guard).
    Assert.Equal(deferred, result.Stops.Any(x => x.PreTripDeferred));
    if (!stale)
      Assert.Equal(
        deferred ? 0 : 15,
        Assert.Single(result.Stops).PreTripMinutes
      );
    Assert.Equal(
      deferred,
      result.Assumptions.Any(x => x.StartsWith("PC · ETA excludes PTI"))
    );
  }

  // The rule's own edges: 50 km exactly qualifies, a status read more than
  // three minutes ago does not, and an unknown distance does not.
  [Fact]
  public void ThePersonalConveyanceApproachNeedsAFreshStatusAndAKnownDistance()
  {
    var moving = State(Plan(10, 600), 0).Progress! with { SpeedMph = 40 };
    DriverDutyStatus Status(int minutesAgo) =>
      new("personalConveyance", null, null, Now.AddMinutes(-minutesAgo));
    var limit = 50 * PersonalConveyanceApproach.MilesPerKilometre;

    Assert.True(
      PersonalConveyanceApproach.Qualifies(
        Status(0),
        moving,
        limit,
        Now.UtcDateTime,
        50
      )
    );
    Assert.False(
      PersonalConveyanceApproach.Qualifies(
        Status(0),
        moving,
        limit + .01,
        Now.UtcDateTime,
        50
      )
    );
    Assert.False(
      PersonalConveyanceApproach.Qualifies(
        Status(4),
        moving,
        3,
        Now.UtcDateTime,
        50
      )
    );
    Assert.False(
      PersonalConveyanceApproach.Qualifies(
        Status(0),
        moving,
        null,
        Now.UtcDateTime,
        50
      )
    );
    Assert.False(
      PersonalConveyanceApproach.Qualifies(
        Status(0),
        moving with
        {
          SpeedMph = null,
        },
        3,
        Now.UtcDateTime,
        50
      )
    );
  }

  // The deferred pre-trip is not dropped: the leg after the approached
  // stop owes it.
  [Fact]
  public void TheDeferredPreTripIsOwedAfterTheApproachedStop()
  {
    var plan = Plan(3, 180);
    plan.Stops =
    [
      new(Guid.NewGuid(), "Pick Up", "", 1, new(35, -80.5)) { Job = "Pick Up" },
      new(Guid.NewGuid(), "Drop Off", "", 2, new(35, -80)) { Job = "Drop Off" },
    ];
    plan.Route = new()
    {
      Miles = 63,
      Seconds = 3780,
      Legs =
      [
        new(3, 180, [new(35, -81), new(35, -80.5)]),
        new(60, 3600, [new(35, -80.5), new(35, -80)]),
      ],
    };
    var state = State(plan, 0);
    state = state with { Progress = state.Progress! with { SpeedMph = 40 } };

    var stops = Service(new Regions())
      .Calculate(state, Fresh("personalConveyance"), Now.UtcDateTime)
      .Stops;

    Assert.Equal(2, stops.Count);
    Assert.True(stops[0].PreTripDeferred);
    Assert.Equal(0, stops[0].PreTripMinutes);
    Assert.False(stops[1].PreTripDeferred);
    Assert.Equal(15, stops[1].PreTripMinutes);
  }

  private static DriverHosClocks Fresh(string status)
  {
    var clocks = Clocks;
    clocks.DriveMs = 11 * 3600000L;
    clocks.ShiftMs = 14 * 3600000L;
    clocks.CurrentDutyStatus = status;
    clocks.UpdatedAt = Now.UtcDateTime;
    return clocks;
  }

  [Fact]
  public void UnsupportedRegionBehindProgressDoesNotBlockRemainingSupportedTravel()
  {
    var plan = Plan(100, 6000);
    var result = Service(new Regions(true))
      .Calculate(State(plan, 60), Clocks, Now.UtcDateTime);
    Assert.Null(result.UnavailableReason);
    Assert.Equal(Now.AddMinutes(40), Assert.Single(result.Stops).Arrival);
    var unavailable = Service(new Regions(true))
      .Calculate(State(plan, 0), Clocks, Now.UtcDateTime);
    Assert.Empty(unavailable.Stops);
    Assert.NotNull(unavailable.UnavailableReason);
  }

  [Fact]
  public void ChangedProgressReusesProfileButUsesOnlyRemainingRoadTime()
  {
    var plan = Plan(120, 7200);
    var service = Service(new Regions());
    var before = Assert.Single(
      service.Calculate(State(plan, 0), Clocks, Now.UtcDateTime).Stops
    );
    var after = Assert.Single(
      service.Calculate(State(plan, 60), Clocks, Now.UtcDateTime).Stops
    );
    Assert.Equal(60, (before.Arrival - after.Arrival).TotalMinutes, 6);
  }

  private static EtaService Service(IRouteRegionLookup regions) =>
    new(
      null!,
      null!,
      regions,
      new(),
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );

  private static RoutePlan Plan(double miles, double seconds) =>
    new()
    {
      Id = Guid.NewGuid(),
      Version = 1,
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Drop Off", "", 1, new(35, -80))],
      Route = new()
      {
        Miles = miles,
        Seconds = seconds,
        Legs = [new(miles, seconds, [new(35, -81), new(35, -80)])],
      },
    };

  private static RoutePlanningState State(RoutePlan plan, double progress) =>
    new(
      new(),
      plan,
      new(
        progress,
        plan.Route.Miles - progress,
        0,
        0,
        false,
        false,
        Now.UtcDateTime,
        new(35, -81 + progress / plan.Route.Miles)
      ),
      null,
      null,
      true
    );

  private sealed class Regions(bool unsupportedStart = false)
    : IRouteRegionLookup
  {
    public RouteRegion Find(RoutePoint point) =>
      new(
        unsupportedStart && point.Longitude < -80.5 ? "" : "US",
        "Etc/UTC",
        false
      );
  }
}
