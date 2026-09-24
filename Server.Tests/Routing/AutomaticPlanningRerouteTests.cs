using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Routing;

// A departure through the real owner: the tracking pass, the saved plan
// and the provider. What is counted here is provider calls, not
// RerouteDecision's verdicts (RerouteReplayTests). One reroute asks the
// provider twice - the new road, and the reconnect of the road shown - so
// counts are taken from a reroute rather than assumed. The truck is past
// its pickup, heading east for the delivery at 40, -79.
public partial class AutomaticPlanningTests
{
  // Confirmed within the cooldown, the departure is kept, not dropped and
  // not sent: the provider is called once the cooldown has passed, at the
  // next pass, without new fixes.
  [Fact]
  public async Task ADepartureConfirmedInTheCooldownCallsOnceItHasPassed()
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await PassAsync(f, 40, -79.5m, start);
    await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(1));
    var before = f.Router.Calls;
    var rerouted = await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(2));
    Assert.True(rerouted.FromCurrentPosition);
    var calls = f.Router.Calls;
    var perReroute = calls - before;

    await PassAsync(f, 40.25m, -79.6m, start.AddMinutes(3));
    var pending = await PassAsync(f, 40.25m, -79.6m, start.AddMinutes(4));

    Assert.Equal(calls, f.Router.Calls);
    Assert.NotNull(pending.Tracking.OffRouteConfirmedAt);
    pending.LastReroutedAt = DateTime.UtcNow.AddMinutes(-3);
    await f.StoreAsync(pending);
    var again = await PassAsync(f, 40.25m, -79.6m, start.AddMinutes(4));
    Assert.Equal(calls + perReroute, f.Router.Calls);
    Assert.Null(again.Tracking.OffRouteConfirmedAt);
    Assert.True(again.Version > pending.Version);
  }

  // A provider that fails leaves the saved plan as it was, departure
  // included; the failure is answered from the two-minute error memory
  // without another call, and the same fixes confirm it again afterwards
  // - once - rather than counting twice.
  [Fact]
  public async Task AFailedRerouteKeepsThePlanAndBacksOffTheProvider()
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await PassAsync(f, 40, -79.5m, start);
    var first = await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(1));
    Assert.Equal(1, first.Tracking.OffRouteFixes);
    var calls = f.Router.Calls;
    f.Router.Fail = true;

    f.Location.UpdatedAt = start.AddMinutes(2);
    f.Db.ChangeTracker.Clear();
    var failed = await f.Service.ForTruckAsync(f.Truck.Id, default);
    f.Db.ChangeTracker.Clear();
    var repeated = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Equal("Route service unavailable.", failed.Message);
    Assert.Equal(failed.Message, repeated.Message);
    Assert.Equal(calls + 1, f.Router.Calls);
    var kept = await SavedAsync(f);
    Assert.Equal(first.Version, kept.Version);
    Assert.Equal(1, kept.Tracking.OffRouteFixes);
    Assert.Null(kept.LastReroutedAt);

    f.Router.Fail = false;
    f.Cache.Compact(1);
    var recovered = await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(2));
    Assert.True(recovered.FromCurrentPosition);
    Assert.Equal(1, recovered.Version - first.Version);
  }

  // The same newest fix at every pass is one observation: a truck whose
  // positions stop arriving while it stands off the road is never given a
  // new road on the strength of one fix.
  [Fact]
  public async Task AFixJudgedOnceIsNotCountedAgainAtLaterPasses()
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await PassAsync(f, 40, -79.5m, start);
    var calls = f.Router.Calls;

    for (var pass = 0; pass < 4; pass++)
      await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(1));

    var saved = await SavedAsync(f);
    Assert.Equal(1, saved.Tracking.OffRouteFixes);
    Assert.Null(saved.Tracking.OffRouteConfirmedAt);
    Assert.Equal(calls, f.Router.Calls);
  }

  // The work changes while the provider answers a departure: the answer
  // is not published, the saved plan and its departure stay as they were,
  // and nothing claims the truck was given a new road.
  [Fact]
  public async Task AWorkChangeDuringADepartureRerouteDiscardsTheAnswer()
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await PassAsync(f, 40, -79.5m, start);
    var first = await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(1));
    f.Router.BeforeCalculate = async () =>
    {
      f.Router.BeforeCalculate = null;
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[1].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Longitude, -78m));
    };
    f.Location.Latitude = 40.1m;
    f.Location.Longitude = -79.4m;
    f.Location.UpdatedAt = start.AddMinutes(2);
    f.Db.ChangeTracker.Clear();

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Plans.AdvanceAutomaticallyAsync(f.Load.Id, default)
    );

    Assert.Contains("work changed", error.Message);
    var kept = await SavedAsync(f);
    Assert.Equal(first.Version, kept.Version);
    Assert.Null(kept.LastReroutedAt);
    Assert.False(kept.FromCurrentPosition);
  }

  // A refusal that says when to try again - another pass holding the
  // truck's planning lock for a few seconds - is not remembered past that
  // time: the next ask tries again instead of repeating it for two minutes.
  [Fact]
  public async Task ARefusalWithARetryTimeIsNotHeldPastIt()
  {
    await using var f = await Fixture.CreateAsync(
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await PassAsync(f, 40, -79.5m, start);
    await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(1));
    f.Router.Fail = true;
    f.Router.FailRetryAfter = DateTime.UtcNow.AddMilliseconds(-1);
    f.Location.UpdatedAt = start.AddMinutes(2);
    f.Db.ChangeTracker.Clear();
    var refused = await f.Service.ForTruckAsync(f.Truck.Id, default);
    Assert.Equal("Route service unavailable.", refused.Message);
    var calls = f.Router.Calls;
    f.Router.Fail = false;

    var retried = await PassAsync(f, 40.1m, -79.4m, start.AddMinutes(2));

    Assert.True(f.Router.Calls > calls);
    Assert.True(retried.FromCurrentPosition);
  }

  private static async Task<RoutePlan> PassAsync(
    Fixture f,
    decimal latitude,
    decimal longitude,
    DateTime at
  )
  {
    f.Location.Latitude = latitude;
    f.Location.Longitude = longitude;
    f.Location.UpdatedAt = at;
    f.Db.ChangeTracker.Clear();
    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);
    Assert.Null(result.Message);
    return result.State!.Plan!;
  }

  private static async Task<RoutePlan> SavedAsync(Fixture f)
  {
    f.Db.ChangeTracker.Clear();
    return (await f.Plans.GetAsync(f.Load.Id, default)).Plan!;
  }
}
