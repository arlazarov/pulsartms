using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelSavedHorizonTests
{
  [Fact]
  public async Task ArrivalAtPendingPickupKeepsEveryStopAndAllowsFuelPlanningWithoutRouteRepair()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Current.Stops[0].PickedUpAt = null;
    await fixture.Db.SaveChangesAsync();
    var plan = fixture.State.Plan!;
    plan.Stops.Insert(0, new(fixture.Current.Stops[0].Id, "Pending pickup", "", 1, new(40, -81)));
    plan.Route.Legs.Insert(0, new(50, 3000, [new(40, -82), new(40, -81)]));
    plan.Route.Legs[1] = SavedFuelHorizonFixture.Route(-81, -79).Legs[0];
    plan.Route.Miles = 150;
    plan.Route.Seconds = 9000;
    plan.Tracking.NextStopId = fixture.Current.Stops[0].Id;
    var state = fixture.State with { Progress = fixture.State.Progress! with { Position = new(40.005, -81) } };

    var result = await fixture.Horizon.BuildAsync(state, state.Profile, default);

    Assert.Equal(4, result.Stops.Count);
    Assert.Equal(0, result.Route.Legs[0].Miles);
    Assert.Equal(300, result.Route.Miles);
    Assert.InRange(result.StartAccessMiles, .5, .6);
    Assert.Equal(fixture.Current.Stops[0].Id, result.Itinerary[0].Stop.Id);
    Assert.Equal(0, result.Itinerary[0].EndMiles);
    Assert.Equal(100, result.Itinerary[1].EndMiles);
    Assert.Null(fixture.Current.Stops[0].PickedUpAt);
    Assert.Empty(plan.Tracking.PassedStopIds);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(.00324, .5)]
  [InlineData(0, 0)]
  [InlineData(.1, -1)]
  public async Task NearbyOriginAccessIsSeparateFromUnchangedSavedRoadGeometry(double latitudeOffset, double accessMiles)
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var state = fixture.State with { Progress = fixture.State.Progress! with { Position = new(40 + latitudeOffset, -80) } };

    var result = await fixture.Horizon.BuildAsync(state, state.Profile, default);

    if (accessMiles < 0) Assert.InRange(result.StartAccessMiles, 10, 11);
    else Assert.Equal(accessMiles, result.StartAccessMiles);
    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(18000, result.Route.Seconds);
    Assert.Equal(40, result.Route.Legs[0].Points[0].Latitude);
    Assert.Equal(-80, result.Route.Legs[0].Points[0].Longitude);
    Assert.Equal(fixture.Current.Stops[1].Id, result.Stops[0].Id);
    Assert.Equal(100, fixture.State.Plan!.Route.Miles);
    Assert.Equal(40, fixture.State.Plan.Route.Legs[0].Points[0].Latitude);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task OriginBeyondFortyMilesFailsWithoutRequestingOrInventingAConnection()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var state = fixture.State with { Progress = fixture.State.Progress! with { Position = new(40.6, -80) } };

    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Horizon.BuildAsync(state, state.Profile, default));

    Assert.Equal(100, fixture.State.Plan!.Route.Miles);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task WiderOriginAllowanceDoesNotRelaxMandatoryDestinationAnchoring()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.State.Plan!.Route.Legs[0].Points[^1] = new(40, -79.02);
    var state = fixture.State with { Progress = fixture.State.Progress! with { Position = new(40.00324, -80) } };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Horizon.BuildAsync(state, state.Profile, default));

    Assert.Contains("confirmed stops", error.Message);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CurrentSavedBaseRoadCanBeTrimmedAtGpsAfterPickupWithoutRequestingAConnection()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.State.Plan!.FromCurrentPosition = false;
    fixture.State.Plan.Route = SavedFuelHorizonFixture.Route(-81, -79);
    fixture.State.Plan.Stops.Insert(0, new(fixture.Current.Stops[0].Id, "Completed pickup", "", 1, new(40, -81)));

    var result = await fixture.Horizon.BuildAsync(fixture.State, fixture.State.Profile, default);

    Assert.Equal(250, result.Route.Miles, 6);
    Assert.Equal(50, result.Route.Legs[0].Miles, 6);
    Assert.Equal(-80, result.Route.Legs[0].Points[0].Longitude, 6);
    Assert.Equal(fixture.Current.Stops[1].Id, result.Stops[0].Id);
    Assert.False(fixture.State.Plan.FromCurrentPosition);
    Assert.Equal(100, fixture.State.Plan.Route.Miles);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task ACurrentBaseRoadDoesNotInventAMissingConnectionBeforeItsFirstPickup()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Current.Stops[0].PickedUpAt = null;
    await fixture.Db.SaveChangesAsync();
    fixture.State.Plan!.FromCurrentPosition = false;
    fixture.State.Plan.Route = SavedFuelHorizonFixture.Route(-81, -79);
    fixture.State.Plan.Stops.Insert(0, new(fixture.Current.Stops[0].Id, "Pending pickup", "", 1, new(40, -81)));

    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Horizon.BuildAsync(fixture.State, fixture.State.Profile, default));

    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CompleteSavedRoadsCoverEveryAssignedLoadWithoutRoutingOrWrites()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var baseline = await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    var connection = await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync();

    var result = await fixture.Horizon.BuildAsync(fixture.State, fixture.State.Profile, default);

    Assert.Equal(new[] { fixture.Current.Id, fixture.Future.Id }, result.DispatchIds);
    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(18000, result.Route.Seconds);
    Assert.Equal(3, result.Stops.Count);
    Assert.Equal(new[] { fixture.Current.Stops[1].Id, fixture.Future.Stops[0].Id, fixture.Future.Stops[1].Id },
      result.Itinerary.Select(stop => stop.Stop.Id));
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(baseline.RouteJson, (await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()).RouteJson);
    Assert.Equal(connection.RouteJson, (await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync()).RouteJson);
  }

  [Theory]
  [InlineData("missing-base")]
  [InlineData("missing-connection")]
  [InlineData("wrong-base-endpoint")]
  [InlineData("wrong-connection-endpoint")]
  [InlineData("unverified-street")]
  [InlineData("off-route-current")]
  [InlineData("changed-profile")]
  public async Task MissingOrInvalidSavedInputsFailWithoutRepairOrProviderCalls(string scenario)
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    if (scenario == "missing-base") fixture.Db.DispatchBaseRoutes.Remove(await fixture.Db.DispatchBaseRoutes.SingleAsync());
    if (scenario == "missing-connection") fixture.Db.DispatchDeadheads.Remove(await fixture.Db.DispatchDeadheads.SingleAsync());
    if (scenario == "wrong-base-endpoint")
      (await fixture.Db.DispatchBaseRoutes.SingleAsync()).RouteJson = RoutePlanStorage.Serialize(SavedFuelHorizonFixture.Route(-78, -76));
    if (scenario == "wrong-connection-endpoint")
      (await fixture.Db.DispatchDeadheads.SingleAsync()).RouteJson = RoutePlanStorage.Serialize(SavedFuelHorizonFixture.Route(-79, -78.1));
    if (scenario == "unverified-street") fixture.Future.Stops[0].Address = "Unverified street";
    if (scenario == "changed-profile") fixture.State.Profile.HeightFeet += 1;
    var state = scenario == "off-route-current"
      ? fixture.State with { Progress = fixture.State.Progress! with { Position = new(41, -80) } } : fixture.State;
    await fixture.Db.SaveChangesAsync();
    var before = await fixture.Db.DispatchBaseRoutes.AsNoTracking().Select(row => row.RouteJson).ToArrayAsync();

    await Assert.ThrowsAsync<RoutePlanningException>(() => fixture.Horizon.BuildAsync(state, state.Profile, default));

    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(before, await fixture.Db.DispatchBaseRoutes.AsNoTracking().Select(row => row.RouteJson).ToArrayAsync());
    Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), entry => entry.State is EntityState.Modified or EntityState.Added);
  }

  [Fact]
  public async Task CompletedFuturePickupReusesItsExistingPathWithoutAddingTheCompletedStop()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Future.Stops[0].PickedUpAt = DateTime.UtcNow.AddMinutes(-1);
    await fixture.Db.SaveChangesAsync();

    var result = await fixture.Horizon.BuildAsync(fixture.State, fixture.State.Profile, default);

    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(2, result.Route.Legs.Count);
    Assert.Equal(200, result.Route.Legs[1].Miles);
    Assert.DoesNotContain(result.Stops, stop => stop.Id == fixture.Future.Stops[0].Id);
    Assert.Contains(result.Route.Legs[1].Points, point => point.Longitude == -78);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CancelledSavedHorizonNeverEntersAProvider()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Horizon.BuildAsync(fixture.State, fixture.State.Profile, cancellation.Token));

    Assert.Equal(0, fixture.Router.Calls);
  }
}
