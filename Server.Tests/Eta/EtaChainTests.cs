using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Eta;
using Domain.Rules.Ports;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaChainTests
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
  private static readonly RoutePoint A = new(35, -81),
    B = new(35, -80),
    C = new(35, -79),
    D = new(35, -78);

  [Fact]
  public void ReceivingDriverCannotBorrowThePreviousDriversClocks()
  {
    var (state, chain) = Example();
    const string reason =
      "ETA unavailable: waiting for the receiving driver's truck assignment and HOS.";
    var forecast = Service()
      .Calculate(
        state,
        Clocks(10),
        Now.UtcDateTime,
        chain: chain with
        {
          CurrentUnavailableReason = reason,
        }
      );
    Assert.Empty(forecast.Stops);
    Assert.Equal(reason, forecast.UnavailableReason);
  }

  [Fact]
  public void FutureStopsCarryCurrentDriveHoursAndBothFacilityDwells()
  {
    var (state, chain) = Example();
    var forecast = Service()
      .Calculate(state, Clocks(3), Now.UtcDateTime, chain: chain);
    Assert.Equal(3, forecast.Stops.Count);
    Assert.Equal(Now.AddHours(1), forecast.Stops[0].Arrival);
    Assert.Equal(Now.AddHours(3), forecast.Stops[0].Departure);
    Assert.Equal(Now.AddHours(4), forecast.Stops[1].Arrival);
    Assert.Equal(Now.AddHours(6), forecast.Stops[1].Departure);
    Assert.InRange(
      (forecast.Stops[2].Arrival - Now).TotalHours,
      18.333,
      18.334
    );
    Assert.Equal(state.Plan!.DispatchId, forecast.Stops[0].DispatchId);
    Assert.All(
      forecast.Stops.Skip(1),
      stop => Assert.Equal(chain.Future[0].DispatchId, stop.DispatchId)
    );
    Assert.Equal(840, forecast.Stops[2].RestMinutes);
  }

  [Fact]
  public void CycleAfterDepartureIncludesAllDrivingButNotSleeperFacilityServices()
  {
    var (state, chain) = Example();
    var forecast = Service()
      .Calculate(state, Clocks(10), Now.UtcDateTime, chain: chain);
    Assert.Equal(
      new[] { 59 * 60, 58 * 60, 56 * 60 },
      forecast.Stops.Select(stop => stop.CycleAfterDeparture!.RemainingMinutes)
    );
    Assert.Equal(60 * 60, forecast.CycleAtCalculation!.RemainingMinutes);
    Assert.False(forecast.CycleAtCalculation.RecapVerified);
    Assert.All(
      forecast.Stops,
      stop =>
      {
        Assert.NotNull(stop.Departure);
        Assert.False(stop.CycleAfterDeparture!.RecapVerified);
        Assert.Null(stop.CycleAfterDeparture.NextRecapAt);
        Assert.Null(stop.CycleAfterDeparture.NextRecapMinutes);
      }
    );
    var future = EtaForecastService.Filter(
      forecast,
      chain.Future[0].DispatchId
    );
    Assert.Equal(forecast.Stops.Skip(1), future.Stops);
    Assert.Equal(
      56 * 60,
      future.Stops[^1].CycleAfterDeparture!.RemainingMinutes
    );
    Assert.Equal(forecast.CycleAtCalculation, future.CycleAtCalculation);
    Assert.Empty(EtaForecastService.Filter(forecast, Guid.NewGuid()).Stops);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void EldAnchorSurvivesMismatchedOrPartialHistoryAcrossLoads(
    bool partial
  )
  {
    var (state, chain) = Example();
    var history = HosForecastFixture.History(
      Now,
      cycleHours: 49 + 29d / 60 + 32d / 3600
    );
    var clocks = Clocks(10, 50 + 2d / 60 + 38d / 3600);
    if (partial)
      history = history with
      {
        Periods = [new(Now.AddHours(-4), Now, "onDuty")],
      };

    var result = Service()
      .Calculate(state, clocks, Now.UtcDateTime, history, chain);

    Assert.Equal(3002, result.CycleAtCalculation!.RemainingMinutes);
    Assert.False(result.CycleAtCalculation.RecapVerified);
    Assert.Equal(
      new int?[] { 2942, 2882, 2762 },
      result.Stops.Select(stop => stop.Hours!.CycleAtArrivalMinutes)
    );
    Assert.All(
      result.Stops,
      stop =>
      {
        Assert.True(stop.Hours!.CycleVerified);
        Assert.Null(stop.Hours.UnavailableReason);
        Assert.False(stop.CycleAfterDeparture!.RecapVerified);
        Assert.Null(stop.CycleAfterDeparture.NextRecapAt);
        Assert.Null(stop.CycleAfterDeparture.NextRecapMinutes);
      }
    );
    var future = EtaForecastService.Filter(result, chain.Future[0].DispatchId);
    Assert.Equal(result.CycleAtCalculation, future.CycleAtCalculation);
    Assert.Equal(result.Stops.Skip(1), future.Stops);
  }

  [Fact]
  public void NextRecapRemainsTheCurrentHistoryBoundaryWhenDeliveryFallsAfterIt()
  {
    var (state, chain) = Example();
    var history = RecapHistory(Now);
    var used = HosTimeline
      .Create(history, Now)!
      .CycleUsed(Now, history.UsCycle!);
    var future = chain.Future[0];
    var pickup = future.Stops[0] with
    {
      ScheduledDate = new(2026, 9, 12),
      ScheduledTime = new(11, 0),
    };
    chain = chain with
    {
      Future = [future with { Stops = [pickup, future.Stops[1]] }],
    };

    var forecast = Service()
      .Calculate(state, Clocks(10, 70 - used), Now.UtcDateTime, history, chain);

    var initial = Assert.IsType<StopCycleForecast>(forecast.CycleAtCalculation);
    Assert.True(initial.RecapVerified);
    Assert.Equal(
      new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(-4)),
      initial.NextRecapAt
    );
    Assert.Equal(185, initial.NextRecapMinutes);
    Assert.Equal("America/New_York", initial.HomeTimeZoneId);
    Assert.True(forecast.Stops[^1].Arrival > initial.NextRecapAt);
    Assert.NotEqual(
      initial.NextRecapAt,
      forecast.Stops[^1].CycleAfterDeparture!.NextRecapAt
    );
    Assert.All(
      new[] { state.Plan!.DispatchId, future.DispatchId },
      dispatchId =>
        Assert.Equal(
          initial,
          EtaForecastService.Filter(forecast, dispatchId).CycleAtCalculation
        )
    );
  }

  [Fact]
  public void CurrentRecapIsCapturedBeforeOngoingRestCrossesItsHomeDayBoundary()
  {
    var now = new DateTimeOffset(2026, 9, 10, 23, 0, 0, TimeSpan.FromHours(-4));
    var (state, _) = Example();
    var history = RecapHistory(now);
    var used = HosTimeline
      .Create(history, now)!
      .CycleUsed(now, history.UsCycle!);
    var clocks = Clocks(10, 70 - used);
    clocks.UpdatedAt = now.UtcDateTime;

    var forecast = Service().Calculate(state, clocks, now.UtcDateTime, history);

    Assert.Contains(
      forecast.Assumptions,
      assumption =>
        assumption.StartsWith(
          "ETA assumes the current rest",
          StringComparison.Ordinal
        )
    );
    var initial = Assert.IsType<StopCycleForecast>(forecast.CycleAtCalculation);
    Assert.Equal(
      new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(-4)),
      initial.NextRecapAt
    );
    Assert.Equal(185, initial.NextRecapMinutes);
    Assert.True(Assert.Single(forecast.Stops).Arrival > initial.NextRecapAt);
    Assert.NotEqual(
      initial.NextRecapAt,
      forecast.Stops[0].CycleAfterDeparture!.NextRecapAt
    );
  }

  [Fact]
  public void AppointmentSlackAbsorbsEarlierDelayWithoutDoubleCountingWaiting()
  {
    var (state, chain) = Example();
    var future = chain.Future[0];
    var scheduled = future.Stops[0] with
    {
      ScheduledDate = DateOnly.FromDateTime(Now.Date),
      ScheduledTime = new(20, 0),
    };
    chain = chain with
    {
      Future = [future with { Stops = [scheduled, future.Stops[1]] }],
    };
    var service = Service();
    var first = service.Calculate(
      state,
      Clocks(10),
      Now.UtcDateTime,
      chain: chain
    );
    state.Plan!.Version++;
    state.Plan.Route.Legs[0] = state.Plan.Route.Legs[0] with { Seconds = 7200 };
    var delayed = service.Calculate(
      state,
      Clocks(10),
      Now.UtcDateTime,
      chain: chain
    );
    Assert.True(delayed.Stops[1].Arrival > first.Stops[1].Arrival);
    Assert.Equal(Now.AddHours(8), first.Stops[1].ServiceStart);
    Assert.Equal(first.Stops[1].Departure, delayed.Stops[1].Departure);
    Assert.Equal(first.Stops[2].Arrival, delayed.Stops[2].Arrival);
    Assert.Equal(0, delayed.Stops[1].LateMinutes);
  }

  [Fact]
  public void MissingConnectionRetainsCurrentEstimatesAndBlocksEveryLaterLoad()
  {
    var (state, chain) = Example();
    var missing = chain.Future[0] with
    {
      Connection = null,
      UnavailableReason = "Saved connection unavailable.",
    };
    var later = chain.Future[0] with { DispatchId = Guid.NewGuid() };
    chain = chain with { Future = [missing, later] };
    var result = Service()
      .Calculate(state, Clocks(10), Now.UtcDateTime, chain: chain);
    Assert.Single(result.Stops);
    Assert.Null(result.UnavailableReason);
    Assert.Equal(2, result.PendingDispatches.Count);
    Assert.Empty(EtaForecastService.Filter(result, later.DispatchId).Stops);
    Assert.NotNull(
      EtaForecastService.Filter(result, later.DispatchId).UnavailableReason
    );
  }

  [Fact]
  public void CurrentFacilityUsesRemainingDwellInsteadOfAddingAnotherFullService()
  {
    var (state, chain) = Example();
    var stop = state.Plan!.Stops[0];
    state = state with
    {
      Progress = state.Progress! with { ProgressMiles = 60, Position = B },
    };
    chain = chain with
    {
      CurrentActivities = new Dictionary<Guid, EtaStopActivity>
      {
        [stop.Id] = new(Now.AddHours(-1).UtcDateTime, null, null, null),
      },
    };
    var result = Service()
      .Calculate(state, Clocks(10), Now.UtcDateTime, chain: chain);
    Assert.Equal(Now.AddHours(-1), result.Stops[0].Arrival);
    Assert.Equal(Now.AddHours(1), result.Stops[0].Departure);
    Assert.Equal(Now.AddHours(2), result.Stops[1].Arrival);
    Assert.Null(result.Stops[0].Hours!.CycleAtArrivalMinutes);
  }

  [Fact]
  public void CompletedFacilityDoesNotAddServiceBeforeTheNextLoad()
  {
    var (state, chain) = Example();
    var stop = state.Plan!.Stops[0];
    state = state with
    {
      Progress = state.Progress! with { ProgressMiles = 60, Position = B },
    };
    chain = chain with
    {
      CurrentActivities = new Dictionary<Guid, EtaStopActivity>
      {
        [stop.Id] = new(
          Now.AddHours(-2).UtcDateTime,
          null,
          Now.UtcDateTime,
          null
        ),
      },
    };
    var result = Service()
      .Calculate(state, Clocks(10), Now.UtcDateTime, chain: chain);
    Assert.Equal(2, result.Stops.Count);
    Assert.Equal(Now.AddHours(1), result.Stops[0].Arrival);
  }

  [Fact]
  public void MissingHistoryDoesNotTurnLowCycleIntoAnAssumedRestart()
  {
    var (state, chain) = Example();
    var result = Service()
      .Calculate(state, Clocks(10, cycle: 3), Now.UtcDateTime, chain: chain);
    Assert.Equal(Now.AddHours(4), result.Stops[1].Arrival);
    Assert.Equal(120, result.Stops[1].RestMinutes);
    Assert.Equal(120, result.Stops[0].CycleAfterDeparture!.RemainingMinutes);
    Assert.False(result.Stops[1].Hours!.CycleVerified);
    Assert.Null(result.Stops[1].Hours!.CycleAtArrivalMinutes);
    Assert.Empty(result.Stops[1].Hours!.Alternatives);
  }

  [Fact]
  public void DistantAppointmentRetainsEarlierStopsAndMarksTheRemainingChainPending()
  {
    var (state, chain) = Example();
    var next = chain.Future[0];
    var pickup = next.Stops[0] with
    {
      ScheduledDate = new(2027, 9, 8),
      ScheduledTime = new(12, 0),
    };
    var later = next with { DispatchId = Guid.NewGuid() };
    chain = chain with
    {
      Future = [next with { Stops = [pickup, next.Stops[1]] }, later],
    };
    var forecast = Service()
      .Calculate(state, Clocks(10), Now.UtcDateTime, chain: chain);
    Assert.Single(forecast.Stops);
    Assert.Equal(state.Plan!.DispatchId, forecast.Stops[0].DispatchId);
    Assert.Equal(2, forecast.PendingDispatches.Count);
    Assert.All(
      forecast.PendingDispatches.Values,
      value => Assert.Contains("90-day", value)
    );
  }

  [Theory]
  [InlineData(0, false)]
  [InlineData(36, true)]
  public void CycleAlternativesReplayThePriorDeliveryAndOnlyOfferAnOnTimeRestart(
    int appointmentHours,
    bool restartFits
  )
  {
    var (state, chain) = Example();
    var next = chain.Future[0];
    var appointment = Now.AddHours(4 + appointmentHours);
    var pickup = next.Stops[0] with
    {
      ScheduledDate = DateOnly.FromDateTime(appointment.Date),
      ScheduledTime = TimeOnly.FromDateTime(appointment.DateTime),
    };
    chain = chain with
    {
      Future = [next with { Stops = [pickup, next.Stops[1]] }],
    };
    var history = HosForecastFixture.History(Now, cycleHours: 1);
    var forecast = Service()
      .Calculate(
        state,
        HosForecastFixture.Clocks(Now, history, drive: 10),
        Now.UtcDateTime,
        history,
        chain
      );

    Assert.Equal(Now.AddHours(1), forecast.Stops[0].Arrival);
    Assert.Equal(Now.AddHours(4), forecast.Stops[1].Arrival);
    Assert.Equal(0, forecast.Stops[0].Hours!.CycleAfterStopMinutes);
    Assert.Null(forecast.Stops[0].Hours!.FirstCycleShortageAt);
    var hours = forecast.Stops[1].Hours!;
    Assert.True(hours.CycleVerified);
    Assert.Equal(Now.AddHours(3), hours.FirstCycleShortageAt);
    Assert.Equal(60, hours.DrivingShortfallMinutes);
    var recap = Assert.Single(
      hours.Alternatives,
      alternative => alternative.Kind == "recap"
    );
    Assert.True(recap.Arrival > forecast.Stops[1].Arrival);
    Assert.True(recap.Departure >= recap.Arrival.AddHours(2));
    Assert.Equal(Now.AddHours(3), recap.RestStartedAt);
    Assert.Equal(
      restartFits,
      hours.Alternatives.Any(alternative => alternative.Kind == "restart")
    );
    Assert.Equal(restartFits, recap.LateMinutes == 0);
    Assert.Equal(
      hours,
      EtaForecastService.Filter(forecast, next.DispatchId).Stops[0].Hours
    );
  }

  [Fact]
  public void VerifiedHistoricalArrivalDoesNotMislabelLiveCycleAsArrivalCycle()
  {
    var (state, chain) = Example();
    var stop = state.Plan!.Stops[0];
    state = state with
    {
      Progress = state.Progress! with { ProgressMiles = 60, Position = B },
    };
    chain = chain with
    {
      CurrentActivities = new Dictionary<Guid, EtaStopActivity>
      {
        [stop.Id] = new(Now.AddHours(-1).UtcDateTime, null, null, null),
      },
    };
    var history = HosForecastFixture.History(Now, cycleHours: 5);
    var forecast = Service()
      .Calculate(
        state,
        HosForecastFixture.Clocks(Now, history, drive: 10),
        Now.UtcDateTime,
        history,
        chain
      );
    var current = forecast.Stops[0];
    Assert.Equal(Now.AddHours(-1), current.Arrival);
    Assert.Null(current.Hours!.CycleAtArrivalMinutes);
    Assert.Equal(300, current.Hours.CurrentCycleMinutes);
    Assert.True(current.Hours.CycleVerified);
    Assert.Equal(300, current.Hours.CycleAfterStopMinutes);
    Assert.Empty(current.Hours.Alternatives);
  }

  [Fact]
  public void CancelledCalculationDoesNotBeginAnyScenario()
  {
    var (state, chain) = Example();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Assert.Throws<OperationCanceledException>(
      () =>
        Service()
          .Calculate(
            state,
            Clocks(10),
            Now.UtcDateTime,
            chain: chain,
            cancellationToken: cancellation.Token
          )
    );
  }

  [Theory]
  [InlineData(12, 2, 60, 20.333333)]
  [InlineData(36, 10, 4, 44.333333)]
  public void FutureAppointmentRestIsCreditedThroughTheWholeLoadChain(
    int waitHours,
    double drive,
    double cycle,
    double arrivalHours
  )
  {
    var (state, chain) = Example();
    var next = chain.Future[0];
    var appointment = Now.AddHours(4 + waitHours).UtcDateTime;
    var pickup = next.Stops[0] with
    {
      ScheduledDate = DateOnly.FromDateTime(appointment),
      ScheduledTime = TimeOnly.FromDateTime(appointment),
    };
    chain = chain with
    {
      Future = [next with { Stops = [pickup, next.Stops[1]] }],
    };
    var result = Service()
      .Calculate(state, Clocks(drive, cycle), Now.UtcDateTime, chain: chain);
    Assert.InRange(
      (result.Stops[^1].Arrival - Now).TotalHours,
      arrivalHours - .001,
      arrivalHours + .001
    );
    Assert.Contains(
      result.Assumptions,
      value => value.StartsWith("Appointment waits", StringComparison.Ordinal)
    );
  }

  private static (RoutePlanningState State, EtaChainPlan Chain) Example()
  {
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Version = 1,
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, B) { Job = "Drop Off" }],
      Route = Road(A, B, 60, 3600),
    };
    var state = new RoutePlanningState(
      new(),
      plan,
      new(0, 60, 3600, 0, false, false, Now.UtcDateTime, A),
      50,
      Now.UtcDateTime,
      true
    );
    var next = new EtaFutureDispatch(
      Guid.NewGuid(),
      [
        new(Guid.NewGuid(), "Pickup", "", 1, C) { Job = "Pick Up" },
        new(Guid.NewGuid(), "Delivery", "", 2, D) { Job = "Drop Off" },
      ],
      EtaRouteTiming.Compile(Road(B, C, 60, 3600), new Regions()),
      EtaRouteTiming.Compile(Road(C, D, 120, 7200), new Regions()),
      null
    );
    return (
      state,
      new("fixture", [next], new Dictionary<Guid, EtaStopActivity>())
    );
  }

  private static TruckRoute Road(
    RoutePoint from,
    RoutePoint to,
    double miles,
    double seconds
  ) =>
    new()
    {
      Miles = miles,
      Seconds = seconds,
      Legs = [new(miles, seconds, [from, to])],
    };

  private static DriverHosClocks Clocks(double drive, double cycle = 60) =>
    new()
    {
      DriveMs = (long)(drive * 3600000),
      ShiftMs = 14 * 3600000L,
      CycleMs = (long)(cycle * 3600000),
      BreakMs = 8 * 3600000L,
      UpdatedAt = Now.UtcDateTime,
    };

  private static EtaService Service() =>
    new(
      null!,
      null!,
      new Regions(),
      new(),
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );

  private static HosHistory RecapHistory(DateTimeOffset now)
  {
    var offset = TimeSpan.FromHours(-4);
    var start = new DateTimeOffset(2026, 8, 23, 0, 0, 0, offset);
    var cursor = start;
    var periods = new List<HosPeriod>();
    for (
      var day = new DateTime(2026, 9, 3);
      day <= now.ToOffset(offset).Date;
      day = day.AddDays(1)
    )
    {
      var on = new DateTimeOffset(day.AddHours(5), offset);
      if (on >= now)
        break;
      var end = on.AddMinutes(day.Day == 3 ? 185 : 9 * 60);
      if (end > now)
        end = now;
      if (cursor < on)
        periods.Add(new(cursor, on, "offDuty"));
      periods.Add(new(on, end, "onDuty"));
      cursor = end;
    }
    if (cursor < now)
      periods.Add(new(cursor, now, "offDuty"));
    return new(
      start,
      now,
      "America/New_York",
      0,
      new(8, 70, 34),
      null,
      periods
    );
  }

  private sealed class Regions : IRouteRegionLookup
  {
    public RouteRegion Find(RoutePoint point) => new("US", "Etc/UTC", false);
  }
}
