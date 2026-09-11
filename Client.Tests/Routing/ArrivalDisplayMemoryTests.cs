using Client.Services;
using Client.Models.DTO.Planning;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class ArrivalDisplayMemoryTests
{
  [Fact]
  public void FirstForecastCanArriveWithHydratedStopDetailsAfterAnEmptyPlaceholder()
  {
    var memory = new ArrivalDisplayMemory();
    var now = DateTime.UnixEpoch;
    var dispatch = Guid.NewGuid();
    var placeholder = new PlanStop(Guid.NewGuid(), "Delivery", "", 1, new(40, -80));
    memory.Update(dispatch, placeholder, null);
    var detailed = placeholder with { Address = "200 Receiving Street", ScheduledDate = new(2026, 9, 10) };
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(detailed.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, detailed, eta);
    Assert.Same(eta, memory.Display(eta, now));
  }

  [Fact]
  public void QuietRefreshKeepsTheOriginalCompleteSnapshotWithoutExtendingItsDeadline()
  {
    var memory = new ArrivalDisplayMemory();
    var now = DateTime.UnixEpoch;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 45, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta);
    Assert.Same(eta, memory.Display(eta, now.AddMinutes(3), refreshing: true));
    memory.Update(dispatch, stop, eta);
    Assert.Same(eta, memory.Display(eta, now.AddMinutes(17).AddTicks(-1), refreshing: true));
    Assert.Null(memory.Display(eta, now.AddMinutes(17), refreshing: true));
    Assert.False(eta.RouteUpdatePending);
    var unavailable = eta with { Stops = [], UnavailableReason = "GPS unavailable" };
    Assert.NotSame(eta, memory.Display(unavailable, now.AddMinutes(1), refreshing: true));
  }

  [Theory]
  [InlineData("address")]
  [InlineData("point")]
  [InlineData("appointment")]
  public void ChangedSameIdStopRejectsTheOldTimestampUntilANewerCompleteForecastArrives(string change)
  {
    var memory = new ArrivalDisplayMemory();
    var now = DateTime.UnixEpoch;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta);
    var changed = change switch
    {
      "address" => stop with { Address = "Other warehouse" },
      "point" => stop with { Point = new(40, -75) },
      _ => stop with { ScheduledDate = new(2026, 9, 10) }
    };
    memory.Update(dispatch, changed, eta);
    Assert.Null(memory.Display(eta, now, refreshing: true));
    var pending = eta with { CalculatedAt = now.AddSeconds(1), RouteUpdatePending = true };
    memory.Update(dispatch, changed, pending);
    Assert.Null(memory.Display(pending, now, refreshing: true));
    var ready = pending with { RouteUpdatePending = false };
    memory.Update(dispatch, changed, ready);
    Assert.Same(ready, memory.Display(ready, now, refreshing: true));
  }

  [Fact]
  public void NewerPendingStopCannotReplaceAnyPartOfTheCompleteSnapshot()
  {
    var memory = new ArrivalDisplayMemory();
    var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var cycle = new StopCycleForecast(217, now.AddDays(1), 185, "America/New_York", true);
    var complete = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        { DispatchId = dispatch, CycleAfterDeparture = cycle }], null, []);
    var pending = complete with { CalculatedAt = now.AddMinutes(1), ValidUntil = now.AddMinutes(3),
      RouteUpdatePending = true, Stops = [complete.Stops[0] with { Arrival = now.AddHours(2), CycleAfterDeparture = null }] };

    memory.Update(dispatch, stop, complete);
    memory.Update(dispatch, stop, pending);
    Assert.Same(complete, memory.Display(pending, now.AddMinutes(1)));
    Assert.Same(cycle, memory.Display(pending, now.AddMinutes(3))!.Stops[0].CycleAfterDeparture);
    Assert.Null(memory.Display(pending, now.AddMinutes(18)));
    var refreshed = pending with { RouteUpdatePending = false,
      Stops = [pending.Stops[0] with { CycleAfterDeparture = cycle with { RemainingMinutes = 200 } }] };
    memory.Update(dispatch, stop, refreshed);
    Assert.Same(refreshed, memory.Display(refreshed, now.AddMinutes(2)));
  }

  [Fact]
  public void MatchingFreshDataCannotBypassCompletionAndDifferentIdentityCannotBeDisplayed()
  {
    var memory = new ArrivalDisplayMemory();
    var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now, "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta, completed: true);
    Assert.Null(memory.Display(eta, now));
    memory.Update(dispatch, stop, eta);
    var differentLoad = eta with { Stops = [eta.Stops[0] with { DispatchId = Guid.NewGuid() }] };
    Assert.Null(memory.Display(differentLoad, now));
    Assert.Null(memory.Display(differentLoad with { RouteUpdatePending = true }, now));
    var unknownLoad = eta with { Stops = [eta.Stops[0] with { DispatchId = Guid.Empty }] };
    Assert.Null(memory.Display(unknownLoad, now));
    Assert.Null(memory.Display(unknownLoad with { RouteUpdatePending = true }, now));
    var differentStop = eta with { Stops = [eta.Stops[0] with { StopId = Guid.NewGuid() }] };
    Assert.Null(memory.Display(differentStop, now));
    Assert.Null(memory.Display(differentStop with { RouteUpdatePending = true }, now));
  }

  [Fact]
  public void OutOfOrderFreshSnapshotCannotRevertTheDisplayedCalculation()
  {
    var memory = new ArrivalDisplayMemory();
    var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var older = new DispatchEta(now, now.AddMinutes(2), [new(stop.Id, now, "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    var newer = older with { CalculatedAt = now.AddSeconds(30), Stops = [older.Stops[0] with { Arrival = now.AddHours(1) }] };
    memory.Update(dispatch, stop, newer);
    memory.Update(dispatch, stop, older);
    Assert.Same(newer, memory.Display(older, now.AddMinutes(1)));
  }

  [Fact]
  public void CurrentEstimateExpiresEvenWhenNoNewPollingResponseArrives()
  {
    var memory = new ArrivalDisplayMemory();
    var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2), [new(stop.Id, now, "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta);
    Assert.Same(eta, memory.Display(eta, now.AddMinutes(1)));
    Assert.Null(memory.Display(eta, now.AddMinutes(2)));
    Assert.Same(eta, memory.Display(eta with { Stops = [], RouteUpdatePending = true }, now.AddMinutes(3)));
    Assert.Null(memory.Display(null, now.AddMinutes(18)));
  }

  [Fact]
  public void PreviousEstimateSurvivesMissingPlanButNotDifferentLoadOrStop()
  {
    var memory = new ArrivalDisplayMemory();
    var now = DateTime.UtcNow;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2), [new(stop.Id, now, "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta);
    memory.Update(dispatch, null, null);
    Assert.Same(eta, memory.PreviousDuringUpdate(null, now.AddMinutes(3)));
    Assert.Null(memory.PreviousDuringUpdate(null, now.AddMinutes(18)));
    memory.Update(dispatch, stop with { Id = Guid.NewGuid() }, null);
    Assert.Null(memory.PreviousDuringUpdate(null, now));
    memory.Update(dispatch, stop, eta);
    memory.Update(Guid.NewGuid(), null, null);
    Assert.Null(memory.Stop);
    Assert.Null(memory.PreviousDuringUpdate(null, now));
  }

  [Fact]
  public void PersistedExpiredForecastHasBoundedUpdatingGraceAndCannotReplaceNewerMemory()
  {
    var memory = new ArrivalDisplayMemory();
    var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var saved = new DispatchEta(now.AddMinutes(-3), now.AddMinutes(-1),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []) { RouteUpdatePending = true };
    memory.Update(dispatch, stop, saved);
    Assert.Same(saved, memory.PreviousDuringUpdate(saved, now));
    Assert.Same(saved, memory.Display(saved, now));
    Assert.Null(memory.Display(saved, now.AddMinutes(15)));
    var newer = saved with { CalculatedAt = now, ValidUntil = now.AddMinutes(2), RouteUpdatePending = false };
    memory.Update(dispatch, stop, newer);
    memory.Update(dispatch, stop, saved);
    Assert.Same(newer, memory.PreviousDuringUpdate(null, now));
    memory.Update(Guid.NewGuid(), stop, null);
    Assert.Null(memory.PreviousDuringUpdate(null, now));
  }

  [Fact]
  public void NonRecalculationFailureDoesNotDisplayOldEstimate()
  {
    var memory = new ArrivalDisplayMemory();
    var now = DateTime.UtcNow;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2), [new(stop.Id, now, "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, []);
    memory.Update(dispatch, stop, eta);
    var missing = new DispatchEta(now, now.AddMinutes(2), [], "GPS unavailable", []);
    Assert.Null(memory.PreviousDuringUpdate(missing, now));
    Assert.Same(eta, memory.PreviousDuringUpdate(missing with { RouteUpdatePending = true }, now));
    memory.Update(memory.DispatchId, null, null, completed: true);
    Assert.Null(memory.Stop);
    Assert.Null(memory.PreviousDuringUpdate(null, now));
  }
}
