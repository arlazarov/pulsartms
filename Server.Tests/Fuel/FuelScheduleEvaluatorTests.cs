using System.Text.Json;
using Application.Features.Eta.Models;
using Application.Features.Eta.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelScheduleEvaluatorTests
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
  private static readonly RoutePoint Start = new(40, -80);

  [Theory]
  [InlineData(null)]
  [InlineData("root-revision")]
  [InlineData("future-native")]
  [InlineData("future-revision")]
  [InlineData("repeated-root")]
  [InlineData("missing-root")]
  public async Task NativeRootAndFollowingLegacyStopsKeepTheirOwnIdentities(
    string? invalid
  )
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60, 60, 60);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = fixture.TruckId,
      DriverId = (await fixture.Db.Trucks.SingleAsync()).DriverId,
      Trip = new() { Id = Guid.NewGuid() },
      Status = "active",
      Revision = 2,
    };
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    state.Plan!.ExecutionLegId = leg.Id;
    state.Plan.AssignmentRevision = leg.Revision;
    stops[0] = stops[0] with
    {
      ExecutionLegId = leg.Id,
      AssignmentRevision = leg.Revision,
      Stop = stops[0].Stop with { Job = "Delivery" },
    };
    stops[2] = stops[2] with { DispatchId = stops[1].DispatchId };
    if (invalid == "root-revision")
      stops[0] = stops[0] with { AssignmentRevision = 1 };
    if (invalid == "future-native")
      stops[1] = stops[1] with { ExecutionLegId = Guid.NewGuid() };
    if (invalid == "future-revision")
      stops[1] = stops[1] with { AssignmentRevision = 1 };
    if (invalid == "repeated-root")
      stops[2] = stops[2] with
      {
        DispatchId = stops[0].DispatchId,
        ExecutionLegId = leg.Id,
        AssignmentRevision = leg.Revision,
      };
    if (invalid == "missing-root")
      stops[0] = stops[0] with { DispatchId = Guid.NewGuid() };

    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );

    Assert.Equal(invalid is null, context.Baseline.Complete);
    Assert.Equal(invalid is null ? 1 : 0, fixture.Hos.ClockCalls);
    Assert.Equal(invalid is null ? 1 : 0, fixture.Hos.HistoryCalls);
    if (invalid is null)
    {
      Assert.Equal(
        stops.Select(stop => (stop.DispatchId, stop.Stop.Id)),
        context.Baseline.Stops.Select(stop => (stop.DispatchId, stop.StopId))
      );
      Assert.All(
        context.Baseline.Stops,
        stop => Assert.NotNull(stop.CandidateArrival)
      );
    }
  }

  [Fact]
  public async Task StationArrivalAfterPickupUsesAppointmentWaitAndOneCapturedHosSnapshot()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60, 60);
    stops[0] = stops[0] with
    {
      Stop = stops[0].Stop with
      {
        ScheduledDate = DateOnly.FromDateTime(Now.AddDays(1).DateTime),
        ScheduledTime = new(9, 0),
      },
    };
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var geometry = new FuelSearchGeometry(baseline);
    var mile = baseline.Legs[0].Miles + baseline.Legs[1].Miles / 2;
    var candidate = new FuelCandidate(
      new() { StationId = Guid.NewGuid(), Point = geometry.At(mile) },
      mile,
      0,
      0,
      3,
      3
    )
    {
      LegIndex = 1,
    };
    var arrival = context.Arrivals(
      baseline,
      [candidate],
      geometry,
      0,
      true,
      default
    )[candidate.VisitKey];
    Assert.True(arrival.Date >= Now.AddDays(1).Date);
    Assert.True(arrival < context.Baseline.Stops[1].CandidateArrival);
    Assert.Equal(1, fixture.Hos.ClockCalls);
    Assert.Equal(1, fixture.Hos.HistoryCalls);
  }

  [Fact]
  public async Task AppointmentWaitingAbsorbsAnEarlierDetourAcrossTheSameClockAndOrderedLoads()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60, 60);
    stops[0] = stops[0] with
    {
      Stop = stops[0].Stop with { ScheduledTime = new(15, 0) },
    };
    stops[1] = stops[1] with
    {
      Stop = stops[1].Stop with { ScheduledTime = new(19, 0) },
    };
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90, 60));
    Assert.True(impact.Complete);
    Assert.True(impact.CycleKnown);
    Assert.Equal(0, impact.AddedMinutes);
    Assert.Equal(0, impact.AddedLateMinutes);
    Assert.Equal(
      30,
      (impact.Stops[0].CandidateArrival - impact.Stops[0].BaselineArrival)!
        .Value
        .TotalMinutes
    );
    Assert.Equal(
      impact.Stops[1].BaselineArrival,
      impact.Stops[1].CandidateArrival
    );
    Assert.Equal(
      stops.Select(stop => (stop.DispatchId, stop.Stop.Id)),
      impact.Stops.Select(stop => (stop.DispatchId, stop.StopId))
    );
  }

  [Fact]
  public async Task DetourAcrossDailyDrivingLimitIncludesOneRestAndOneFuelAllowance()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(
      Now,
      drive: 1
    );
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90));
    Assert.True(impact.Complete);
    Assert.InRange(impact.AddedMinutes!.Value, 650, 651);
    Assert.Equal(Now.AddHours(1), impact.Stops[0].BaselineArrival);
    Assert.InRange(
      (impact.Stops[0].CandidateArrival!.Value - Now).TotalMinutes,
      710,
      711
    );
  }

  [Fact]
  public async Task CrossingHomeMidnightUsesRealRecapInsteadOfSubtractingAllDetourTime()
  {
    var now = Now.AddHours(11);
    await using var fixture = await FuelScheduleFixture.CreateAsync(
      now,
      cycle: 1,
      firstDayHours: 185d / 60
    );
    var (state, baseline, stops) = Inputs(fixture.TruckId, now, 30);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90));
    Assert.True(impact.CycleKnown);
    Assert.InRange(
      impact.Stops[0].BaselineCycleAtArrivalMinutes!.Value,
      29,
      30
    );
    Assert.InRange(
      impact.Stops[0].CandidateCycleAtArrivalMinutes!.Value,
      154,
      155
    );
    Assert.False(impact.CycleShort);
  }

  [Fact]
  public async Task CycleShortageIsReportedWithoutInsertingAnAutomaticRestart()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(
      Now,
      cycle: 1
    );
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 30);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 120));
    Assert.True(impact.CycleKnown);
    Assert.False(impact.BaselineCycleShort);
    Assert.True(impact.CycleShort);
    Assert.InRange(impact.AddedMinutes!.Value, 90, 91);
    Assert.InRange(
      impact.Stops[0].CandidateCycleAtArrivalMinutes!.Value,
      -61,
      -60
    );
  }

  [Fact]
  public async Task ExistingLatenessAndCycleShortageRemainVisibleWithoutBeingCountedAsNew()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(
      Now,
      cycle: 1
    );
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 120);
    stops[0] = stops[0] with
    {
      Stop = stops[0].Stop with { ScheduledTime = new(11, 0) },
    };
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 150));
    Assert.True(impact.BaselineCycleShort);
    Assert.True(impact.CycleShort);
    Assert.Equal(180, impact.Stops[0].BaselineLateMinutes);
    Assert.Equal(210, impact.Stops[0].CandidateLateMinutes);
    Assert.Equal(30, impact.AddedLateMinutes);
  }

  [Theory]
  [InlineData("missing", false)]
  [InlineData("stale", false)]
  [InlineData("history", true)]
  [InlineData("old-history", true)]
  public async Task MissingOrStaleInputsCannotProduceKnownCycleFeasibility(
    string input,
    bool complete
  )
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    if (input == "missing")
      fixture.Hos.Clocks = null;
    if (input == "stale")
      fixture.Hos.Clocks!.UpdatedAt = Now.AddMinutes(-4).UtcDateTime;
    if (input == "history")
      fixture.Hos.History = null;
    if (input == "old-history")
      fixture.Hos.History = fixture.Hos.History! with
      {
        Through = Now.AddMinutes(-4),
      };
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90));
    Assert.Equal(complete, impact.Complete);
    Assert.False(impact.CycleKnown);
    Assert.False(impact.CycleShort);
    Assert.NotNull(impact.UnavailableReason);
    Assert.Null(impact.Stops[0].CandidateCycleAtArrivalMinutes);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExpectedHosFailuresLeaveFuelScheduleUnknownWithoutFailingTheFuelCalculation(
    bool historyOnly
  )
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    if (historyOnly)
      fixture.Hos.HistoryFailure = new HttpRequestException("Unavailable");
    else
      fixture.Hos.ClockFailure = new HttpRequestException("Unavailable");
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90));
    Assert.Equal(historyOnly, impact.Complete);
    Assert.False(impact.CycleKnown);
    Assert.NotNull(impact.UnavailableReason);
    Assert.Equal(1, fixture.Hos.ClockCalls);
    Assert.Equal(historyOnly ? 1 : 0, fixture.Hos.HistoryCalls);
  }

  [Fact]
  public async Task ProviderTimeoutIsUnknownButAnUnexpectedFailureIsNotSilentlySuppressed()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    fixture.Hos.ClockFailure = new TaskCanceledException("Provider timeout");
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    Assert.False(context.Baseline.Complete);
    fixture.Hos.ClockFailure = new InvalidOperationException(
      "Unexpected failure"
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        fixture.Evaluator.PrepareAsync(
          state,
          baseline,
          stops,
          Now.UtcDateTime,
          default
        )
    );
  }

  [Fact]
  public async Task MissingAppointmentKeepsAdditionalLatenessUnknownEvenWhenRoadTimesAreComplete()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    stops[0] = stops[0] with
    {
      Stop = stops[0].Stop with { ScheduledDate = null, ScheduledTime = null },
    };
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var impact = context.Evaluate(Road(stops, 90));
    Assert.True(impact.Complete);
    Assert.Equal(30, impact.AddedMinutes);
    Assert.Null(impact.AddedLateMinutes);
    Assert.Null(impact.Stops[0].CandidateLateMinutes);
  }

  [Fact]
  public async Task VariantsReuseFrozenHosInputsWithoutChangingTheOriginalPlanOrPublishedForecast()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var saved = new DispatchEta(
      Now.UtcDateTime,
      Now.AddMinutes(2).UtcDateTime,
      [],
      null,
      []
    );
    var entry = new EtaMemory.Entry("live", saved);
    fixture.Memory.Results[state.Plan!.DispatchId] = entry;
    fixture.Memory.Viewed[state.Plan.DispatchId] = Now.UtcDateTime;
    state = state with { Eta = saved };
    var original = JsonSerializer.Serialize(state);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var expected = context.Evaluate(Road(stops, 90));
    fixture.Hos.Clocks!.DriveMs = 0;
    fixture.Hos.Clocks.CycleMs = 0;
    ((List<HosPeriod>)fixture.Hos.History!.Periods).Clear();
    for (var index = 0; index < 9; index++)
      Assert.Equal(
        JsonSerializer.Serialize(expected),
        JsonSerializer.Serialize(context.Evaluate(Road(stops, 90)))
      );
    Assert.Equal(1, fixture.Hos.ClockCalls);
    Assert.Equal(1, fixture.Hos.HistoryCalls);
    Assert.Same(entry, Assert.Single(fixture.Memory.Results).Value);
    Assert.Equal(Now.UtcDateTime, Assert.Single(fixture.Memory.Viewed).Value);
    Assert.Equal(original, JsonSerializer.Serialize(state));
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task TwelveRouteBoundIncludesThePreparedBaseline()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    Assert.True(context.Baseline.Complete);
    for (var index = 0; index < 11; index++)
      Assert.True(context.Evaluate(Road(stops, 90)).Complete);
    var exhausted = context.Evaluate(Road(stops, 90));
    Assert.False(exhausted.Complete);
    Assert.Contains("limit", exhausted.UnavailableReason);
    Assert.Equal(1, fixture.Hos.ClockCalls);
    Assert.Equal(1, fixture.Hos.HistoryCalls);
  }

  [Theory]
  [InlineData("nan")]
  [InlineData("boundary")]
  [InlineData("count")]
  [InlineData("horizon")]
  public async Task InvalidCompleteRoutesAreUnknownRatherThanPartialOptimisticForecasts(
    string invalid
  )
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    var route = Road(stops, 90);
    if (invalid == "nan")
      route.Seconds = double.NaN;
    if (invalid == "boundary")
      route.Legs[0].Points[^1] = new(45, -90);
    if (invalid == "count")
      route.Legs.Clear();
    if (invalid == "horizon")
      route.Seconds = 91 * 24 * 3600;
    var result = context.Evaluate(route);
    Assert.False(result.Complete);
    Assert.False(result.CycleKnown);
    Assert.Null(result.AddedMinutes);
    Assert.NotNull(result.UnavailableReason);
  }

  [Fact]
  public async Task InvalidBaselineStopsBeforeRequestingAnyHosData()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    stops[0] = stops[0] with { EndMiles = 900 };
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    Assert.False(context.Baseline.Complete);
    Assert.Equal(0, fixture.Hos.ClockCalls);
    Assert.Equal(0, fixture.Hos.HistoryCalls);
  }

  [Fact]
  public async Task CancelledCandidateDoesNotSpendTheRemainingPreviewBudget()
  {
    await using var fixture = await FuelScheduleFixture.CreateAsync(Now);
    var (state, baseline, stops) = Inputs(fixture.TruckId, Now, 60);
    var context = await fixture.Evaluator.PrepareAsync(
      state,
      baseline,
      stops,
      Now.UtcDateTime,
      default
    );
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Assert.Throws<OperationCanceledException>(
      () => context.Evaluate(Road(stops, 90), cancellation.Token)
    );
    for (var index = 0; index < 11; index++)
      Assert.True(context.Evaluate(Road(stops, 90)).Complete);
  }

  private static (RoutePlanningState, TruckRoute, FuelItineraryStop[]) Inputs(
    Guid truck,
    DateTimeOffset now,
    params double[] miles
  )
  {
    double end = 0;
    var stops = miles
      .Select(
        (distance, index) =>
          new FuelItineraryStop(
            Guid.NewGuid(),
            new(
              Guid.NewGuid(),
              "Facility",
              "",
              index + 1,
              new(40, -79.8 + index * .2)
            )
            {
              Job = index == 0 && miles.Length > 1 ? "Pick Up" : "Drop Off",
              ScheduledDate = DateOnly.FromDateTime(now.Date),
              ScheduledTime = new(23, 59),
            },
            end += distance
          )
      )
      .ToArray();
    var road = Road(stops, miles);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      DispatchId = stops[0].DispatchId,
      Route = road,
      Stops = stops.Select(stop => stop.Stop).ToList(),
      Version = 1,
      FromCurrentPosition = true,
    };
    return (
      new(
        new(),
        plan,
        new(
          0,
          road.Miles,
          road.Seconds,
          0,
          false,
          false,
          now.UtcDateTime,
          Start
        ),
        50,
        now.UtcDateTime,
        true
      ),
      road,
      stops
    );
  }

  private static TruckRoute Road(
    IReadOnlyList<FuelItineraryStop> stops,
    params double[] miles
  ) =>
    new()
    {
      Miles = miles.Sum(),
      Seconds = miles.Sum() * 60,
      Legs = miles
        .Select(
          (distance, index) =>
            new RouteLeg(
              distance,
              distance * 60,
              [
                index == 0 ? Start : stops[index - 1].Stop.Point,
                stops[index].Stop.Point,
              ]
            )
        )
        .ToList(),
    };
}
