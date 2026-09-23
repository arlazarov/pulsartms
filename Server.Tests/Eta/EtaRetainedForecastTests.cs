using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Policies;
using Infrastructure.Integrations.GeoTimeZone;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

// A forecast that has gone out of date for the same work stays on screen,
// marked as updating, until the next one lands. The Dispatch board already
// reads saved forecasts this way; the map read the same work as having no
// forecast at all. Other work never inherits one.
[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaRetainedForecastTests
{
  [Fact]
  public void AnExpiredForecastForTheSameWorkIsShownAsUpdating()
  {
    using var memory = new EtaMemory();
    var service = Service(memory);
    var state = State();
    var now = DateTime.UtcNow;
    service.Record(
      state,
      "a",
      Forecast(now.AddMinutes(-5), now.AddSeconds(-1))
    );

    var shown = Assert.IsType<DispatchEta>(service.GetCached(state));

    Assert.True(shown.RouteUpdatePending);
    Assert.Equal(now.AddSeconds(-1), shown.ValidUntil);
    // Kept, and due: the worker replaces it rather than a reader losing it.
    Assert.True(memory.Results.ContainsKey(Key(state)));
    Assert.Contains(Key(state), memory.Due(now));
  }

  [Fact]
  public void AConfirmedDeviationKeepsTheForecastUntilItsReplacementLands()
  {
    using var memory = new EtaMemory();
    var service = Service(memory);
    var before = State();
    var now = DateTime.UtcNow;
    service.Record(before, "a", Forecast(now, now.AddMinutes(10)));
    Assert.False(service.GetCached(before)!.RouteUpdatePending);
    Assert.DoesNotContain(Key(before), memory.Due(now));

    // Off the planned road: the same work, a road that no longer holds.
    var deviated = before with
    {
      Progress = before.Progress! with { OffRoute = true },
    };
    var shown = Assert.IsType<DispatchEta>(service.GetCached(deviated));
    Assert.True(shown.RouteUpdatePending);
    Assert.True(memory.Results[Key(before)].Superseded);
    Assert.Contains(Key(before), memory.Due(now));

    // A failed refresh leaves it as it was; the reader keeps seeing it.
    Assert.True(service.GetCached(deviated)!.RouteUpdatePending);

    service.Record(deviated, "b", Forecast(now, now.AddMinutes(10)));
    Assert.False(service.GetCached(deviated)!.RouteUpdatePending);
    Assert.DoesNotContain(Key(before), memory.Due(now));
  }

  [Theory]
  [InlineData("assignment")]
  [InlineData("stops")]
  [InlineData("truck")]
  public void AForecastForOtherWorkIsNeverShown(string change)
  {
    using var memory = new EtaMemory();
    var service = Service(memory);
    var state = State();
    var now = DateTime.UtcNow;
    service.Record(state, "a", Forecast(now, now.AddMinutes(10)));
    var plan = state.Plan!;
    var other = change switch
    {
      "assignment" => Copy(plan, x => x.AssignmentRevision++),
      "stops" => Copy(
        plan,
        x => x.Stops = [new(Guid.NewGuid(), "Other", "", 1, new(36, -80))]
      ),
      _ => Copy(plan, x => x.TruckId = Guid.NewGuid()),
    };

    Assert.Null(service.GetCached(state with { Plan = other }));
    Assert.False(memory.Results.ContainsKey(Key(state)));
  }

  [Fact]
  public void ACurrentForecastIsReturnedAsItWasCalculated()
  {
    using var memory = new EtaMemory();
    var service = Service(memory);
    var state = State();
    var now = DateTime.UtcNow;
    var forecast = Forecast(now, now.AddMinutes(10));
    service.Record(state, "a", forecast);
    Assert.Same(forecast, service.GetCached(state));
  }

  private static Guid Key(RoutePlanningState state) =>
    state.Plan!.ExecutionLegId ?? state.Plan.DispatchId;

  private static EtaService Service(EtaMemory memory) =>
    new(
      null!,
      null!,
      new RouteRegionLookup(),
      memory,
      new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions())
    );

  private static DispatchEta Forecast(DateTime calculated, DateTime valid) =>
    new(calculated, valid, [], null, []);

  private static RoutePlanningState State()
  {
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      AssignmentRevision = 3,
      Version = 2,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, new(35, -80))],
    };
    return new(
      new(),
      plan,
      new(0, 100, 7200, 30, false, false, DateTime.UtcNow, new(35, -81)),
      50,
      DateTime.UtcNow,
      true
    );
  }

  private static RoutePlan Copy(RoutePlan plan, Action<RoutePlan> change)
  {
    var copy = new RoutePlan
    {
      Id = plan.Id,
      DispatchId = plan.DispatchId,
      ExecutionLegId = plan.ExecutionLegId,
      TruckId = plan.TruckId,
      AssignmentRevision = plan.AssignmentRevision,
      Version = plan.Version,
      Stops = plan.Stops.ToList(),
    };
    change(copy);
    return copy;
  }
}
