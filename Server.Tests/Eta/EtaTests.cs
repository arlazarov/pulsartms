using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Eta;
using Infrastructure.Integrations.GeoTimeZone;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public class EtaTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    6,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  private static DriverHosClocks Clocks(
    double drive = 11,
    double shift = 14,
    double cycle = 70,
    double brk = 8
  ) =>
    new()
    {
      DriveMs = (long)(drive * 3600000),
      ShiftMs = (long)(shift * 3600000),
      CycleMs = (long)(cycle * 3600000),
      BreakMs = (long)(brk * 3600000),
      UpdatedAt = Start.UtcDateTime,
    };

  [Fact]
  public void UsesAvailableHoursWithoutUnnecessaryBreak()
  {
    var c = new HosTravelClock(Start, Clocks(), "US");
    c.Drive(8, "US");
    Assert.Equal(Start.AddHours(8), c.Now);
  }

  [Fact]
  public void ThreeKilometresDoNotForceABreakBeforeArrival()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80.97);
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, b)],
      Route = new TruckRoute { Legs = [new(2, 180, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 2, 180, 0, false, false, Start.UtcDateTime, a),
      50,
      Start.UtcDateTime,
      true
    );
    var stop = Assert.Single(
      new EtaService(
        null!,
        null!,
        new RouteRegionLookup(),
        new EtaMemory(),
        new PlanningTestServices.NoHos(),
        Options.Create(new EtaPlanningOptions())
      )
        .Calculate(state, Clocks(drive: 9, shift: 12), Start.UtcDateTime)
        .Stops
    );
    Assert.Equal(0, stop.RestMinutes);
    Assert.Equal(0, stop.PreTripMinutes);
    Assert.InRange((stop.Arrival - Start).TotalMinutes, 2.99, 3.01);
  }

  [Fact]
  public void PriorShiftDrivingDoesNotForceAPlanningBreakForTheLastThreeKilometres()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80.97);
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, b)],
      Route = new TruckRoute { Legs = [new(2, 180, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 2, 180, 0, false, false, Start.UtcDateTime, a),
      50,
      Start.UtcDateTime,
      true
    );
    var stop = Assert.Single(
      new EtaService(
        null!,
        null!,
        new RouteRegionLookup(),
        new EtaMemory(),
        new PlanningTestServices.NoHos(),
        Options.Create(new EtaPlanningOptions())
      )
        .Calculate(state, Clocks(drive: 3, shift: 6, brk: 8), Start.UtcDateTime)
        .Stops
    );
    Assert.Equal(0, stop.RestMinutes);
    Assert.Equal(0, stop.PreTripMinutes);
    Assert.InRange((stop.Arrival - Start).TotalMinutes, 2.99, 3.01);
  }

  [Fact]
  public void AddsUsBreakAfterEightHours()
  {
    var c = new HosTravelClock(Start, Clocks(), "US");
    c.Drive(9, "US");
    Assert.Equal(Start.AddHours(9.5), c.Now);
  }

  [Fact]
  public void ExhaustedDriveRequiresDailyRest()
  {
    var c = new HosTravelClock(Start, Clocks(drive: 1), "US");
    c.Drive(2, "US");
    Assert.Equal(Start.AddHours(12), c.Now);
  }

  [Fact]
  public void BreakConsumesShiftWindow()
  {
    var c = new HosTravelClock(Start, Clocks(shift: .25, brk: 0), "US");
    c.Drive(1, "US");
    Assert.Equal(Start.AddHours(11.5), c.Now);
  }

  [Fact]
  public void RoadEtaDoesNotAssumeARestartForAnUnknownCycle()
  {
    var c = new HosTravelClock(Start, Clocks(cycle: 1), "US");
    c.Drive(2, "US");
    Assert.Equal(Start.AddHours(2), c.Now);
    Assert.False(c.CycleFeasibility.Verified);
    Assert.Null(c.CycleResumeAt);
  }

  [Fact]
  public void CanadaUsesThirteenHoursAndConservativeDailyRest()
  {
    var c = new HosTravelClock(Start, Clocks(drive: 13), "CA");
    c.Drive(14, "CA");
    Assert.Equal(Start.AddHours(25), c.Now);
  }

  [Fact]
  public void BorderDoesNotResetHours()
  {
    var c = new HosTravelClock(Start, Clocks(drive: 13), "CA");
    c.Drive(10, "CA");
    c.Drive(2, "US");
    Assert.True(c.RestHours >= 10);
  }

  [Fact]
  public void FuelStopQualifiesForBreakButNotDailyReset()
  {
    var c = new HosTravelClock(Start, Clocks(brk: 1), "US");
    c.Drive(1, "US");
    c.Service(.5);
    c.Drive(1, "US");
    Assert.Equal(Start.AddHours(2.5), c.Now);
  }

  [Fact]
  public void AppointmentsUseStopTimezoneAndRejectAmbiguousDst()
  {
    var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    Assert.Equal(
      21,
      EtaWalk
        .Appointment(new(2026, 9, 10), new(14, 0), zone)!
        .Value.UtcDateTime.Hour
    );
    Assert.Null(EtaWalk.Appointment(new(2026, 11, 1), new(1, 30), zone));
  }

  [Fact]
  public void ServiceReturnsDestinationLocalEtaAndLateness()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80);
    var id = Guid.NewGuid();
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops =
      [
        new(id, "Delivery", "", 1, b)
        {
          ScheduledDate = new(2026, 9, 6),
          ScheduledTime = new(9, 0),
        },
      ],
      Route = new TruckRoute { Legs = [new(100, 7200, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 100, 7200, 0, false, false, Start.UtcDateTime, a),
      50,
      Start.UtcDateTime,
      true
    );
    var service = new EtaService(
      null!,
      null!,
      new RouteRegionLookup(),
      new EtaMemory(),
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );
    var result = service.Calculate(state, Clocks(), Start.UtcDateTime);
    Assert.Equal(80, Assert.Single(result.Stops).LateMinutes);
    Assert.Equal(Start.UtcDateTime.AddMinutes(2), result.ValidUntil);
    var missingHos = service.Calculate(state, null, Start.UtcDateTime);
    Assert.Empty(missingHos.Stops);
    Assert.False(missingHos.RouteUpdatePending);
    Assert.False(result.RouteUpdatePending);
  }

  [Fact]
  public void OffRouteEtaUsesSavedRouteWithAConservativeReturn()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80);
    var id = Guid.NewGuid();
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [new(id, "Delivery", "", 1, b)],
      Route = new TruckRoute { Legs = [new(100, 7200, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 100, 7200, 6, true, false, Start.UtcDateTime, new(35.1, -81)),
      50,
      Start.UtcDateTime,
      true
    );
    var stop = Assert.Single(
      new EtaService(
        null!,
        null!,
        new RouteRegionLookup(),
        new EtaMemory(),
        new PlanningTestServices.NoHos(),
        Options.Create(new EtaPlanningOptions())
      )
        .Calculate(state, Clocks(), Start.UtcDateTime)
        .Stops
    );
    Assert.InRange(stop.DrivingMinutes, 127, 128);
  }

  [Fact]
  public void ExcessiveOffRouteDistanceWaitsForARouteUpdate()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80);
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, b)],
      Route = new TruckRoute { Legs = [new(100, 7200, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 100, 7200, 30, true, false, Start.UtcDateTime, new(35.1, -81)),
      50,
      Start.UtcDateTime,
      true
    );
    var result = new EtaService(
      null!,
      null!,
      new RouteRegionLookup(),
      new EtaMemory(),
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    ).Calculate(state, Clocks(), Start.UtcDateTime);
    Assert.Empty(result.Stops);
    Assert.Contains("off-route", result.UnavailableReason);
    Assert.True(result.RouteUpdatePending);
  }

  [Fact]
  public void MemoryEvictsIdleEntriesAndRefreshesDueResults()
  {
    var memory = new EtaMemory();
    var id = Guid.NewGuid();
    memory.Viewed[id] = Start.UtcDateTime;
    memory.Results[id] = new(
      "x",
      new(Start.UtcDateTime, Start.UtcDateTime.AddMinutes(2), [], null, [])
    );
    Assert.Empty(memory.Due(Start.UtcDateTime.AddSeconds(119)));
    Assert.Single(memory.Due(Start.UtcDateTime.AddMinutes(2)));
    Assert.Empty(memory.Due(Start.UtcDateTime.AddMinutes(11)));
    Assert.Empty(memory.Results);
  }

  [Fact]
  public void EtaIncludesRemainingCurrentRestAndLaterDailyRest()
  {
    var a = new RoutePoint(35, -81);
    var b = new RoutePoint(35, -80);
    var id = Guid.NewGuid();
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [new(id, "Delivery", "", 1, b)],
      Route = new TruckRoute { Legs = [new(700, 12 * 3600, [a, b])] },
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 700, 12 * 3600, 0, false, false, Start.UtcDateTime, a),
      50,
      Start.UtcDateTime,
      true
    );
    var clocks = Clocks(drive: 0, shift: 0, cycle: 54);
    clocks.CurrentDutyStatus = "sleeperBerth";
    var history = new HosHistory(
      Start.AddHours(-8),
      Start,
      "Etc/UTC",
      0,
      new(8, 70, 34),
      null,
      [
        new(Start.AddHours(-8), Start.AddHours(-5), "driving"),
        new(Start.AddHours(-5), Start.AddHours(-4), "offDuty"),
        new(Start.AddHours(-4), Start.AddHours(-3), "personalConveyance"),
        new(Start.AddHours(-3), Start, "sleeperBerth"),
      ]
    );
    var result = new EtaService(
      null!,
      null!,
      new RouteRegionLookup(),
      new EtaMemory(),
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    ).Calculate(state, clocks, Start.UtcDateTime, history);
    var stop = Assert.Single(result.Stops);
    Assert.InRange(stop.RestMinutes, 930, 931);
    Assert.InRange(stop.DrivingMinutes, 720, 721);
    Assert.Equal(30, stop.PreTripMinutes);
    Assert.Equal(300, result.DutyStatus!.TenHourRestRemainingMinutes);
    Assert.InRange((stop.Arrival - Start).TotalHours, 28.16, 28.17);
  }

  [Theory]
  [InlineData(42.3314, -83.0458, "US")]
  [InlineData(42.3149, -83.0364, "CA")]
  [InlineData(37.6819, -121.768, "US")]
  [InlineData(43.6532, -79.3832, "CA")]
  public void DetectsCountryAndTimezoneAtLocation(
    double lat,
    double lon,
    string country
  )
  {
    Assert.Equal(country, new RouteRegionLookup().Find(new(lat, lon)).Country);
  }
}
