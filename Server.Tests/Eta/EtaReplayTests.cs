using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
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
