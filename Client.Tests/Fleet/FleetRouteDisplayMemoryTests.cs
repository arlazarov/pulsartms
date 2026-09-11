using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetRouteDisplayMemoryTests
{
  private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PendingAndNullForecastsKeepTheLastDisplayedPairWithoutMutatingTheResponse(bool missing)
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    memory.RecordProgress(790, 210);
    var pending = saved with { Progress = null, Eta = missing ? null : Pending() };
    memory.Update(pending, Now.AddMinutes(3));
    var display = memory.Display(pending, Now.AddMinutes(3))!;
    Assert.Equal(saved.Eta!.Stops, display.Eta!.Stops);
    Assert.Equal(saved.Eta.ValidUntil, display.Eta.ValidUntil);
    Assert.True(display.Eta.RouteUpdatePending);
    Assert.Equal(790, display.Progress!.RemainingMiles);
    Assert.Equal(210, display.Progress.ProgressMiles);
    Assert.Null(pending.Progress);
    Assert.True(missing ? pending.Eta is null : pending.Eta!.Stops.Count == 0);

    var ready = saved with { Progress = saved.Progress! with { RemainingMiles = 780, ProgressMiles = 220 },
      Eta = saved.Eta with { CalculatedAt = Now.AddMinutes(4), ValidUntil = Now.AddMinutes(6) } };
    memory.Update(ready, Now.AddMinutes(4));
    Assert.False(memory.IsRetaining(ready, Now.AddMinutes(4)));
    Assert.Same(ready, memory.Display(ready, Now.AddMinutes(4)));
  }

  [Fact]
  public void RepeatedPendingResponsesCannotSlideTheOriginalGraceDeadline()
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    var pending = saved with { Progress = null, Eta = Pending() };
    memory.Update(pending, Now.AddMinutes(3));
    var repeated = pending with { Eta = Pending() with { CalculatedAt = Now.AddMinutes(16), ValidUntil = Now.AddMinutes(18) } };
    memory.Update(repeated, Now.AddMinutes(16));
    var before = memory.Display(repeated, Now.AddMinutes(17).AddTicks(-1))!;
    Assert.Equal(saved.Eta!.ValidUntil, before.Eta!.ValidUntil);
    Assert.NotNull(before.Progress);
    var expired = memory.Display(repeated, Now.AddMinutes(17))!;
    Assert.Null(expired.Eta);
    Assert.Null(expired.Progress);
    Assert.False(FleetRouteDisplayMemory.CanDisplay(before.Eta, Now.AddMinutes(17)));
  }

  [Fact]
  public void ExplicitUnavailableForecastDoesNotReuseEarlierEstimates()
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    var unavailable = saved with { Progress = null, Eta = Pending() with { RouteUpdatePending = false } };
    memory.Update(unavailable, Now.AddMinutes(1));
    Assert.Same(unavailable, memory.Display(unavailable, Now.AddMinutes(1)));
    Assert.False(memory.IsRetaining(unavailable, Now.AddMinutes(1)));
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("dispatch")]
  [InlineData("stop")]
  [InlineData("passed")]
  [InlineData("complete")]
  [InlineData("address")]
  [InlineData("point")]
  [InlineData("appointment")]
  public void ChangedRouteIdentityNeverReceivesThePreviousPair(string changed)
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    var plan = saved.Plan!;
    switch (changed)
    {
      case "truck": plan.TruckId = Guid.NewGuid(); break;
      case "dispatch": plan.DispatchId = Guid.NewGuid(); break;
      case "stop": plan.Tracking.NextStopId = Guid.NewGuid(); break;
      case "passed": plan.Tracking.PassedStopIds.Add(Guid.NewGuid()); break;
      case "complete": plan.Tracking.AllStopsPassed = true; break;
      case "address": plan.Stops[0] = plan.Stops[0] with { Address = "Changed destination" }; break;
      case "point": plan.Stops[0] = plan.Stops[0] with { Point = new(43, -78) }; break;
      case "appointment": plan.Stops[0] = plan.Stops[0] with { ScheduledDate = new(2026, 9, 10) }; break;
    }
    var pending = saved with { Progress = null, Eta = Pending() };
    Assert.False(memory.Matches(pending));
    memory.Update(pending, Now.AddMinutes(1));
    var display = memory.Display(pending, Now.AddMinutes(1))!;
    Assert.Empty(display.Eta!.Stops);
    Assert.Null(display.Progress);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void GeometryOnlyChangesRetainEtaAndVisibleDistancesWithoutReusingOldProgressCoordinates(bool newPlan)
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    memory.RecordProgress(790, 210);
    var plan = saved.Plan!;
    if (newPlan) plan.Id = Guid.NewGuid();
    else plan.Version++;
    var pending = saved with { Eta = null, Progress = saved.Progress! with { ProgressMiles = 20, RemainingMiles = 850 } };
    Assert.True(memory.Matches(pending));
    Assert.False(memory.MatchesGeometry(pending));
    memory.Update(pending, Now.AddMinutes(3));
    var display = memory.Display(pending, Now.AddMinutes(3))!;
    Assert.Equal(saved.Eta!.Stops, display.Eta!.Stops);
    Assert.Equal(saved.Eta.ValidUntil, display.Eta.ValidUntil);
    Assert.Equal(790, memory.RetainedRemainingMiles);
    Assert.Same(pending.Progress, display.Progress);
    Assert.Equal(20, display.Progress!.ProgressMiles);
  }

  [Fact]
  public void InFlightReadUsesOriginalGraceBeforeAnyPendingResponseArrives()
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    Assert.False(memory.IsRetaining(saved, Now.AddMinutes(3)));
    var display = memory.Display(saved, Now.AddMinutes(3), refreshing: true)!;
    Assert.True(display.Eta!.RouteUpdatePending);
    Assert.Equal(saved.Eta!.ValidUntil, display.Eta.ValidUntil);
    Assert.True(FleetRouteDisplayMemory.CanDisplay(display.Eta, Now.AddMinutes(3)));
    Assert.False(memory.IsRetaining(saved, Now.AddMinutes(17), refreshing: true));
    Assert.False(FleetRouteDisplayMemory.CanDisplay(saved.Eta, Now.AddMinutes(17), refreshing: true));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ChangedDestinationRejectsTheResentOldForecastUntilACompleteNewCalculation(bool pending)
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    saved.Plan!.Stops[0] = saved.Plan.Stops[0] with { Address = "New destination" };
    var outdated = saved with { Eta = saved.Eta! with { RouteUpdatePending = pending } };
    memory.Update(outdated, Now.AddSeconds(10));
    Assert.Null(memory.Display(outdated, Now.AddSeconds(10))!.Eta);
    memory.Update(outdated, Now.AddSeconds(20));
    Assert.Null(memory.Display(outdated, Now.AddSeconds(20))!.Eta);
    var ready = saved with { Eta = saved.Eta! with { CalculatedAt = Now.AddSeconds(30) } };
    memory.Update(ready, Now.AddSeconds(30));
    Assert.Same(ready.Eta, memory.Display(ready, Now.AddSeconds(30))!.Eta);
  }

  [Fact]
  public void DeselectingClearsMemoryEvenWhenTheSameRouteIsSelectedAgain()
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    memory.Update(saved, Now);
    memory.Update(null, Now);
    var pending = saved with { Progress = null, Eta = Pending() };
    memory.Update(pending, Now);
    Assert.Empty(memory.Display(pending, Now)!.Eta!.Stops);
  }

  [Fact]
  public void PersistedPendingForecastUsesItsOriginalBoundWithoutNeedingAnEarlierRender()
  {
    var memory = new FleetRouteDisplayMemory();
    var saved = State();
    var pending = saved with { Eta = saved.Eta! with { RouteUpdatePending = true } };
    memory.Update(pending, Now.AddMinutes(3));
    Assert.Equal(saved.Eta!.Stops, memory.Display(pending, Now.AddMinutes(3))!.Eta!.Stops);
    Assert.Null(memory.Display(pending, Now.AddMinutes(17))!.Eta);
    Assert.False(FleetRouteDisplayMemory.CanDisplay(saved.Eta, Now.AddMinutes(2)));
  }

  [Fact]
  public void PendingWithoutAnEarlierForecastPreservesActualMileage()
  {
    var memory = new FleetRouteDisplayMemory();
    var pending = State() with { Eta = Pending() };
    memory.Update(pending, Now);
    Assert.Same(pending, memory.Display(pending, Now));
    Assert.Equal(800, memory.Display(pending, Now)!.Progress!.RemainingMiles);
    Assert.False(memory.IsRetaining(pending, Now));
  }

  private static DispatchEta Pending() => new(Now, Now.AddMinutes(2), [], "Route updating", []) { RouteUpdatePending = true };

  private static RoutePlanningState State()
  {
    var stop = Guid.NewGuid();
    var dispatch = Guid.NewGuid();
    return new(new(), new() { Id = Guid.NewGuid(), TruckId = Guid.NewGuid(), DispatchId = dispatch, Version = 1,
      Stops = [new(stop, "Destination", "Original address", 1, new(42, -77))],
      Tracking = new() { NextStopId = stop } }, new(200, 800, 1000, 0, false, false, Now, null), null, null, true)
    {
      Eta = new(Now, Now.AddMinutes(2), [new(stop, Now.AddHours(10), "UTC", null, 0, 600, 0) { DispatchId = dispatch }], null, [])
    };
  }
}
