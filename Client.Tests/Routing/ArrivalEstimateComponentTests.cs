using Client.Shared.DriverStatus.ArrivalEstimate;
using Client.Shared.DriverStatus;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class ArrivalEstimateComponentTests
{
  [Fact]
  public void SummaryAndDetailsCanBeSeparatedWithoutChangingTheDefaultDisplay()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, clock.GetUtcNow().AddHours(1).ToOffset(TimeSpan.FromHours(-4)), "America/Toronto", null, 25, 60, 0)], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.Contains("local · UTC-04:00", component.Markup);
    Assert.Contains("Late by 25m", component.Markup);

    component.Render(p => p.Add(x => x.ShowDetails, false));
    Assert.Contains("ETA", component.Markup);
    Assert.Contains("Late by 25m", component.Markup);
    Assert.DoesNotContain("UTC", component.Markup);

    component.Render(p => p.Add(x => x.ShowSummary, false).Add(x => x.ShowDetails, true));
    Assert.Equal("Arrival time zone UTC-04:00", component.Find(".arrival-estimate__timezone").TextContent);
    Assert.DoesNotContain("ETA", component.Markup);
    Assert.DoesNotContain("Late by", component.Markup);
  }

  [Fact]
  public void PassiveRerenderAtTheDeadlineDoesNotTurnTheSameForecastIntoAPendingResponse()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
    clock.Advance(TimeSpan.FromMinutes(2));
    component.Render(p => p.Add(x => x.Eta, eta));
    Assert.Empty(component.FindAll(".arrival-estimate"));
    component.Render(p => p.Add(x => x.Eta, eta with { Stops = eta.Stops.ToArray() }));
    Assert.Empty(component.FindAll(".arrival-estimate"));
    Assert.False(eta.RouteUpdatePending);
  }

  [Theory]
  [InlineData("ETA unavailable: waiting for the saved preceding connection.", false)]
  [InlineData("ETA is recalculating.", true)]
  public void TechnicalUnavailableReasonsNeverBecomeVisibleTextOrAnEmptyWrapper(string reason, bool pending)
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2), [], reason, []) { RouteUpdatePending = pending };
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));

    Assert.DoesNotContain(reason, component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate"));

    component.Render(p => p.Add(x => x.Stop, stop with { Job = "Pickup", ScheduledDate = new(2026, 9, 9) }));
    Assert.Single(component.FindAll(".arrival-estimate__appointment"));
    Assert.DoesNotContain(reason, component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate small"));
  }

  [Fact]
  public void InFlightRefreshKeepsVisibleEtaThroughExpiryAndReplacesAllValuesTogether()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 45, 60, 0) { DispatchId = dispatch }], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.DispatchId, dispatch)
      .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    var previous = component.Markup;
    component.Render(p => p.Add(x => x.Refreshing, true));
    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render();
    Assert.Equal(previous, component.Markup);
    Assert.False(eta.RouteUpdatePending);
    var refreshed = eta with { CalculatedAt = now.AddMinutes(3), ValidUntil = now.AddMinutes(5),
      Stops = [eta.Stops[0] with { Arrival = now.AddHours(2), LateMinutes = 15 }] };
    component.Render(p => p.Add(x => x.Eta, refreshed).Add(x => x.Refreshing, false));
    Assert.Contains("Sep 8 · 02:00 PM", component.Markup);
    Assert.Contains("Late by 15m", component.Markup);
    Assert.DoesNotContain("Sep 8 · 01:00 PM", component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);
  }

  [Fact]
  public void PendingIncompleteSnapshotKeepsArrivalAndDutyUntilTheReplacementIsReady()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0) { DispatchId = dispatch }], null, [])
      { DutyStatus = new("driving", now.AddMinutes(-20), null, now) };
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.DispatchId, dispatch)
      .Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    var previousMarkup = component.Markup;
    component.Render(p => p.Add(x => x.Eta, eta with { CalculatedAt = now.AddMinutes(1),
      RouteUpdatePending = true, DutyStatus = null, Stops = [eta.Stops[0] with { Arrival = now.AddHours(2) }] }));
    Assert.Contains("Sep 8 · 01:00 PM", component.Markup);
    Assert.DoesNotContain("Sep 8 · 02:00 PM", component.Markup);
    Assert.Contains("Driving", component.Find(".driver-duty").TextContent);
    Assert.Equal(previousMarkup, component.Markup);
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
    Assert.DoesNotContain("Updating", component.Markup);
    component.Render(p => p.Add(x => x.Completed, true));
    Assert.Empty(component.FindAll(".arrival-estimate"));
  }

  [Fact]
  public void PreviousLatenessKeepsItsPresentationUntilTheNewEstimateIsReady()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var complete = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, 125, 60, 0) { DispatchId = dispatch }], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.DispatchId, dispatch)
      .Add(x => x.Stop, stop).Add(x => x.Eta, complete));
    Assert.Contains("Late by 2h 05m", component.Find(".arrival-estimate__late").TextContent);
    var previousMarkup = component.Markup;
    clock.Advance(TimeSpan.FromMinutes(3));
    var pending = complete with { CalculatedAt = now.AddMinutes(3), RouteUpdatePending = true, Stops = [] };
    component.Render(p => p.Add(x => x.Eta, pending));
    Assert.Equal(previousMarkup, component.Markup);
    Assert.Equal("Late by 2h 05m", component.Find(".arrival-estimate__late").TextContent);
    Assert.DoesNotContain("Updating", component.Markup);

    var refreshed = complete with { CalculatedAt = now.AddMinutes(3), ValidUntil = now.AddMinutes(5),
      Stops = [complete.Stops[0] with { LateMinutes = 15 }] };
    component.Render(p => p.Add(x => x.Eta, refreshed));
    Assert.Equal("Late by 15m", component.Find(".arrival-estimate__late").TextContent);
    Assert.Empty(component.FindAll(".arrival-estimate__previous-late"));
    Assert.DoesNotContain("Updating", component.Markup);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(30)]
  public void UnexpiredPendingForecastKeepsItsResultWithinTheDisplayGrace(int lateMinutes)
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), "UTC", null, lateMinutes, 60, 0)], null, []) { RouteUpdatePending = true };
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Single(component.FindAll(lateMinutes > 0 ? ".arrival-estimate__late" : ".arrival-estimate__ontime"));
    Assert.Empty(component.FindAll(".arrival-estimate__previous-late"));
    clock.Advance(TimeSpan.FromMinutes(17));
    component.Render();
    Assert.DoesNotContain("ETA", component.Markup);
  }

  [Fact]
  public void HidingAppointmentKeepsEtaAndItsBoundedRecalculationGrace()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80))
    {
      Job = "Pick Up", ScheduledDate = new(2026, 9, 9), ScheduledTime = new(7, 0),
      ScheduledDate2 = new(2026, 9, 9), ScheduledTime2 = new(14, 0)
    };
    var eta = new DispatchEta(now, now.AddMinutes(2),
      [new(stop.Id, new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero), "UTC", null, 0, 60, 0)], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.Contains("Sep 9 · 07:00 AM – 02:00 PM", component.Find(".arrival-estimate__appointment").TextContent);

    component.Render(p => p.Add(x => x.ShowAppointment, false));
    Assert.Empty(component.FindAll(".arrival-estimate__appointment"));
    Assert.Contains("Sep 9 · 08:00 AM", component.Find(".arrival-estimate").TextContent);
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
    var previousMarkup = component.Markup;

    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render(p => p.Add(x => x.Eta, eta with { Stops = [], RouteUpdatePending = true }));
    Assert.Empty(component.FindAll(".arrival-estimate__appointment"));
    Assert.Contains("Sep 9 · 08:00 AM", component.Find(".arrival-estimate").TextContent);
    Assert.Equal(previousMarkup, component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));

    clock.Advance(TimeSpan.FromMinutes(15));
    component.Render(p => p.Add(x => x.Eta, eta));
    Assert.Empty(component.FindAll(".arrival-estimate"));
    component.Render(p => p.Add(x => x.ShowAppointment, true));
    Assert.Single(component.FindAll(".arrival-estimate__appointment"));
    Assert.DoesNotContain("ETA", component.Markup);
  }

  [Fact]
  public void HiddenAppointmentWithoutAnEstimateDoesNotLeaveAnEmptyWrapper()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var stop = new PlanStop(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80))
    {
      ScheduledDate = new(2026, 9, 9), ScheduledTime = new(7, 0)
    };
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.ShowAppointment, false));
    Assert.Empty(component.FindAll(".arrival-estimate"));
  }

  [Fact]
  public void PersistedExpiredForecastKeepsItsResultWithinTheDisplayGrace()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now.AddMinutes(-3), now.AddMinutes(-1),
      [new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)], null, []) { RouteUpdatePending = true };
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.Contains("ETA", component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
  }

  [Fact]
  public void OldEstimateKeepsItsPresentationDuringRecalculationButDisappearsAfterGrace()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
    context.Services.AddSingleton<TimeProvider>(clock);
    var now = clock.GetUtcNow().UtcDateTime;
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -80));
    var eta = new DispatchEta(now, now.AddMinutes(2), [new(stop.Id, now, "UTC", null, 0, 60, 0)], null, []);
    var component = context.Render<ArrivalEstimate>(p => p.Add(x => x.Stop, stop).Add(x => x.Eta, eta));
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render(p => p.Add(x => x.Eta, eta with { Stops = [], RouteUpdatePending = true }));
    Assert.Contains("ETA", component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Single(component.FindAll(".arrival-estimate__ontime"));
    clock.Advance(TimeSpan.FromMinutes(15));
    component.Render(p => p.Add(x => x.Eta, eta));
    Assert.DoesNotContain("ETA", component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
  }
}
