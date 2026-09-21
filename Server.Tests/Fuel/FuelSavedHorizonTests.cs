using System.Text.Json;
using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Models;
using Application.Features.Fuel.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelSavedHorizonTests
{
  [Theory]
  [InlineData(true, false)]
  [InlineData(false, false)]
  [InlineData(true, true)]
  public async Task FuelSkipsOnlyACompletedRouteWithMatchingInputs(
    bool completed,
    bool stale
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var current = await f.ReceiveCurrentAsync();
    current.Status = "planned";
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "current-crew",
    };
    f.Db.Drivers.Add(driver);
    current.DriverId = driver.Id;
    await f.Db.SaveChangesAsync();
    var profile = await f.PrepareCalculationAsync();
    var earlier = new Load
    {
      Id = Guid.NewGuid(),
      TruckId = f.State.Plan!.TruckId,
      Status = "in_transit",
      LoadNumber = 100,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 40,
          Longitude = -83,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Latitude = 40,
          Longitude = -82,
        },
      ],
    };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new() { Id = Guid.NewGuid() },
      TruckId = f.State.Plan.TruckId,
      Status = "active",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(earlier.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = earlier.Id,
          StartVisitId = earlier.Stops[0].Id,
          EndVisitId = earlier.Stops[1].Id,
        },
      ],
    };
    f.Db.Dispatches.Add(earlier);
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    var work = await f.Services.Routes.LoadAsync(earlier.Id, default, leg.Id);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = earlier.Id,
      ExecutionLegId = leg.Id,
      AssignmentRevision = 1,
      TruckId = f.State.Plan.TruckId,
      Profile = profile,
      Tracking = new() { AllStopsPassed = completed },
    };
    f.Db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = earlier.Id,
        ExecutionLegId = leg.Id,
        AssignmentRevision = 1,
        TruckId = plan.TruckId,
        InputHash = RoutePlanInputs.Hash(work, profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      }
    );
    if (stale)
      leg.Stops[0].Latitude = 39;
    await f.Db.SaveChangesAsync();

    var captured = await f.Services.PlanningInputs.ReadFreshAsync(
      plan.TruckId,
      default
    );
    Assert.Equal(
      completed && !stale ? current.Id : leg.Id,
      captured!.CurrentWork!.ExecutionLegId
    );
    Assert.Equal(
      completed && !stale ? driver.Id : (Guid?)null,
      captured.DriverId
    );
    var eta = await f.Services.EtaInputs.DescribeAsync(plan.TruckId, default);
    Assert.NotNull(eta);
    Assert.Equal(captured.DriverId, eta.DriverId);
    Assert.Equal(
      completed && !stale ? current.Id : leg.Id,
      eta.RootExecutionLegId
    );

    async Task<FuelCalculationResult> Calculate() =>
      await f.Services.Fuel.ResetAsync(
        f.Current.Id,
        null,
        default,
        current.Id,
        current.Revision
      );
    if (completed && !stale)
    {
      var result = await Calculate();
      Assert.Equal(current.Id, result.Plan!.ExecutionLegId);
      Assert.NotNull(
        await new TruckFuelPlanStore(f.Db).ReadAsync(
          plan.TruckId,
          false,
          default
        )
      );
    }
    else
      await Assert.ThrowsAsync<RoutePlanningException>(Calculate);
    Assert.Equal("active", leg.Status);
    Assert.Equal(1, leg.Revision);
  }

  [Fact]
  public async Task PlannedConflictCanCalculateResetAndReopenFuel()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var leg = await f.ReceiveCurrentAsync();
    leg.Status = "planned";
    leg.SourceReviewReason = "Trailer conflicts with another assignment.";
    await f.Db.SaveChangesAsync();
    var profile = await f.PrepareCalculationAsync();
    var first = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile)
      {
        ExecutionLegId = leg.Id,
        AssignmentRevision = leg.Revision,
      },
      default
    );
    var reset = await f.Services.Fuel.ResetAsync(
      f.Current.Id,
      first.Plan!.CalculatedAt,
      default,
      leg.Id,
      leg.Revision
    );
    var saved = await new TruckFuelPlanStore(f.Db).ReadAsync(
      f.State.Plan!.TruckId,
      true,
      default
    );
    Assert.NotNull(saved);
    Assert.Equal(reset.Plan!.CalculatedAt, saved.CalculatedAt);
    Assert.Equal(leg.Id, saved.RootExecutionLegId);
    var accepted = await f.Db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
    Assert.Equal("planned", accepted.Status);
    Assert.Equal(leg.Revision, accepted.Revision);
    Assert.Equal(
      "Trailer conflicts with another assignment.",
      accepted.SourceReviewReason
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CommitInvalidatesTheExactRouteScopeRepopulatedBeforeCommit(
    bool received
  )
  {
    var boundary = new FuelCommitFailureProbe();
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync(
      boundary
    );
    var leg = received ? await fixture.ReceiveCurrentAsync() : null;
    var profile = await fixture.PrepareCalculationAsync();
    var previous = await fixture
      .Db.DispatchRoutePlans.AsNoTracking()
      .SingleAsync();
    var key = RoutePlanStore.CacheKey(fixture.Current.Id, leg?.Id);
    var repopulated = false;
    boundary.SnapshotWrittenAsync = async () =>
    {
      Assert.NotNull(fixture.Db.Database.CurrentTransaction);
      var cached = await fixture.Services.Reads.GetAsync(
        key,
        "value",
        () => Task.FromResult<DispatchRoutePlan?>(previous)
      );
      Assert.Equal(previous.PlanJson, cached!.PlanJson);
      repopulated = true;
    };

    var fuel = await fixture.Services.Fuel.BuildAsync(
      fixture.Current.Id,
      new(profile)
      {
        ExecutionLegId = leg?.Id,
        AssignmentRevision = leg?.Revision,
      },
      default
    );

    Assert.True(repopulated);
    var load = await fixture.Services.Routes.LoadAsync(
      fixture.Current.Id,
      default,
      leg?.Id
    );
    var read = await fixture.Services.Routes.GetAsync(load, default);
    Assert.Equal(fuel.Plan!.CalculatedAt, read.Plan!.FuelPlan!.CalculatedAt);
    Assert.Equal(leg?.Id, read.Plan.FuelPlan.ExecutionLegId);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task FullCalculationCommitsAndReopensEveryAssignedScope(
    bool received,
    bool pricedDestination
  )
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var leg = received ? await fixture.ReceiveCurrentAsync() : null;
    var profile = await fixture.PrepareCalculationAsync();
    if (pricedDestination)
    {
      var date = FuelPricingDate.FromUtc(DateTime.UtcNow);
      fixture.Stations.Add(
        new(
          Guid.NewGuid(),
          "destination",
          "Destination fuel",
          "Street",
          "City",
          "PA",
          "",
          "US",
          40,
          -77,
          [new("USD", "Diesel", 4, 3.5m, .5m, date, date, 3, "US gal")]
        )
      );
    }
    var baseJson = (
      await fixture.Db.DispatchBaseRoutes.SingleAsync()
    ).RouteJson;
    var connectionJson = (
      await fixture.Db.DispatchDeadheads.SingleAsync()
    ).RouteJson;

    var result = await fixture.Services.Fuel.BuildAsync(
      fixture.Current.Id,
      new(profile)
      {
        ExecutionLegId = leg?.Id,
        AssignmentRevision = leg?.Revision,
      },
      default
    );

    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await new TruckFuelPlanStore(fixture.Db).ReadAsync(
        fixture.State.Plan!.TruckId,
        true,
        default
      )
    );
    Assert.Equal(
      new[] { fixture.Current.Id, fixture.Future.Id },
      result.Plan!.DispatchIds
    );
    Assert.Equal(result.Plan!.CalculatedAt, saved.Plan!.CalculatedAt);
    Assert.Equal(leg?.Id, saved.RootExecutionLegId);
    Assert.Equal(leg?.Revision ?? 0, saved.AssignmentRevision);
    Assert.Equal(leg?.Id, saved.Stops[0].ExecutionLegId);
    Assert.Equal(leg?.Revision ?? 0, saved.Stops[0].AssignmentRevision);
    Assert.All(
      saved.Stops.Skip(1),
      stop =>
      {
        Assert.Equal(fixture.Future.Id, stop.DispatchId);
        Assert.Null(stop.ExecutionLegId);
        Assert.Equal(0, stop.AssignmentRevision);
      }
    );
    Assert.Null(saved.CheckedRoute);
    Assert.Equal(300, saved.BaselineRoute!.Miles);
    Assert.Equal(3, result.Plan!.StopArrivals.Count);
    Assert.All(
      result.Plan!.StopArrivals,
      stop => Assert.True(stop.Gallons > 0)
    );
    Assert.Equal(0, fixture.Router.Calls);
    var policy = Assert.IsType<FuelArrivalPolicy>(saved.Plan.ArrivalPolicy);
    if (!pricedDestination)
    {
      Assert.Empty(result.Plan!.Stops);
      Assert.Equal(125, policy.MinimumGallons);
      Assert.Equal(policy.MinimumGallons, policy.TargetGallons);
      Assert.Equal(0, policy.ReplacementPriceUsd);
      Assert.Equal(Guid.Empty, policy.EscapeStationId);
      Assert.Equal(
        200 - 300 / profile.Mpg!.Value,
        result.Plan!.ArrivalGallons,
        8
      );
      Assert.Equal(0, result.Plan!.ExpectedFutureFuelCostUsd);
    }
    else
      Assert.True(policy.ReplacementPriceUsd > 0);
    var compatibility = JsonSerializer.Deserialize<RoutePlan>(
      (
        await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
      ).PlanJson,
      RoutingJson.Options
    )!;
    Assert.Equal(
      result.Plan!.CalculatedAt,
      compatibility.FuelPlan!.CalculatedAt
    );
    Assert.Equal(100, compatibility.Route.Miles);
    Assert.Equal(result.Plan!.DispatchIds, compatibility.FuelPlan!.DispatchIds);
    Assert.Equal(
      baseJson,
      (await fixture.Db.DispatchBaseRoutes.SingleAsync()).RouteJson
    );
    Assert.Equal(
      connectionJson,
      (await fixture.Db.DispatchDeadheads.SingleAsync()).RouteJson
    );
    Assert.Equal(1, await fixture.Db.Set<TruckFuelPlan>().CountAsync());
  }

  [Fact]
  public async Task ConfirmedHookKeepsFutureLoadsOnSavedRoadsWithoutWrites()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var leg = await fixture.ReceiveCurrentAsync();
    var baseline = await fixture
      .Db.DispatchBaseRoutes.AsNoTracking()
      .SingleAsync();
    var connection = await fixture
      .Db.DispatchDeadheads.AsNoTracking()
      .SingleAsync();
    var native = await fixture.Services.Routes.LoadAsync(
      fixture.Current.Id,
      default,
      leg.Id,
      fixture.State.Plan!.TruckId
    );
    var future = await fixture.Services.Routes.LoadAsync(
      fixture.Future.Id,
      default,
      null,
      fixture.State.Plan.TruckId
    );
    var pair = DeadheadConnection.Find(future, [native]);
    Assert.NotNull(pair);
    Assert.Equal(connection.InputHash, pair.Signature(fixture.State.Profile));
    Assert.NotNull(pair.ReadRoute(connection, fixture.State.Profile));

    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );

    Assert.Equal(
      new[] { fixture.Current.Id, fixture.Future.Id },
      result.DispatchIds
    );
    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(3, result.Stops.Count);
    Assert.Equal(leg.Id, result.Itinerary[0].ExecutionLegId);
    Assert.Equal(leg.Revision, result.Itinerary[0].AssignmentRevision);
    Assert.Equal(fixture.Current.Stops[^1].Id, result.Itinerary[0].Stop.Id);
    Assert.All(
      result.Itinerary.Skip(1),
      stop =>
      {
        Assert.Equal(fixture.Future.Id, stop.DispatchId);
        Assert.Null(stop.ExecutionLegId);
        Assert.Equal(0, stop.AssignmentRevision);
      }
    );
    Assert.Equal(0, fixture.Router.Calls);
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
    Assert.Equal(
      baseline.RouteJson,
      (
        await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()
      ).RouteJson
    );
    Assert.Equal(
      connection.RouteJson,
      (
        await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync()
      ).RouteJson
    );
  }

  [Fact]
  public async Task ChangedFutureTruckInvalidatesTheSavedMixedScopeHorizon()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    await fixture.ReceiveCurrentAsync();
    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );
    var response = await fixture.Services.Sender.Send(
      new GetDispatchBoardQuery(
        TruckId: fixture.State.Plan!.TruckId,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false,
        IncludeOverdue: true
      )
    );
    var loads = response.Response!.Items.Single().Dispatches;
    Assert.True(
      FuelPlanProjection.RemainingStopsMatch(
        result.Itinerary,
        fixture.Current.Id,
        result.Stops[0].Id,
        loads
      )
    );
    loads.Single(load => load.Id == fixture.Future.Id).TruckId = Guid.NewGuid();

    Assert.False(
      FuelPlanProjection.RemainingStopsMatch(
        result.Itinerary,
        fixture.Current.Id,
        result.Stops[0].Id,
        loads
      )
    );
    Assert.Equal(0, fixture.Router.Calls);
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task ArrivalAtPendingPickupKeepsEveryStopAndAllowsFuelPlanningWithoutRouteRepair()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Current.Stops[0].PickedUpAt = null;
    await fixture.Db.SaveChangesAsync();
    var plan = fixture.State.Plan!;
    plan.Stops.Insert(
      0,
      new(fixture.Current.Stops[0].Id, "Pending pickup", "", 1, new(40, -81))
    );
    plan.Route.Legs.Insert(0, new(50, 3000, [new(40, -82), new(40, -81)]));
    plan.Route.Legs[1] = SavedFuelHorizonFixture.Route(-81, -79).Legs[0];
    plan.Route.Miles = 150;
    plan.Route.Seconds = 9000;
    plan.Tracking.NextStopId = fixture.Current.Stops[0].Id;
    var state = fixture.State with
    {
      Progress = fixture.State.Progress! with { Position = new(40.005, -81) },
    };

    var result = await fixture.Horizon.BuildAsync(
      state,
      state.Profile,
      default
    );

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
  public async Task NearbyOriginAccessIsSeparateFromUnchangedSavedRoadGeometry(
    double latitudeOffset,
    double accessMiles
  )
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var state = fixture.State with
    {
      Progress = fixture.State.Progress! with
      {
        Position = new(40 + latitudeOffset, -80),
      },
    };

    var result = await fixture.Horizon.BuildAsync(
      state,
      state.Profile,
      default
    );

    if (accessMiles < 0)
      Assert.InRange(result.StartAccessMiles, 10, 11);
    else
      Assert.Equal(accessMiles, result.StartAccessMiles);
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
    var state = fixture.State with
    {
      Progress = fixture.State.Progress! with { Position = new(40.6, -80) },
    };

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Horizon.BuildAsync(state, state.Profile, default)
    );

    Assert.Equal(100, fixture.State.Plan!.Route.Miles);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task WiderOriginAllowanceDoesNotRelaxMandatoryDestinationAnchoring()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.State.Plan!.Route.Legs[0].Points[^1] = new(40, -79.02);
    var state = fixture.State with
    {
      Progress = fixture.State.Progress! with { Position = new(40.00324, -80) },
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Horizon.BuildAsync(state, state.Profile, default)
    );

    Assert.Contains("confirmed stops", error.Message);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task ConfirmedSourceDoesNotReplaceTheSavedResolvedRoutePoint()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Current.Stops[1].Longitude = -78.98m;
    fixture.Future.Stops[^1].DeliveredAt = DateTime.UtcNow;
    await fixture.Db.SaveChangesAsync();

    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );

    Assert.Equal(-79, result.Stops[0].Point.Longitude);
    Assert.Equal(-79, result.Route.Legs[0].Points[^1].Longitude);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CurrentSavedBaseRoadCanBeTrimmedAtGpsAfterPickupWithoutRequestingAConnection()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.State.Plan!.FromCurrentPosition = false;
    fixture.State.Plan.Route = SavedFuelHorizonFixture.Route(-81, -79);
    fixture.State.Plan.Stops.Insert(
      0,
      new(fixture.Current.Stops[0].Id, "Completed pickup", "", 1, new(40, -81))
    );

    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );

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
    fixture.State.Plan.Stops.Insert(
      0,
      new(fixture.Current.Stops[0].Id, "Pending pickup", "", 1, new(40, -81))
    );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Horizon.BuildAsync(
          fixture.State,
          fixture.State.Profile,
          default
        )
    );

    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CompleteSavedRoadsCoverEveryAssignedLoadWithoutRoutingOrWrites()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    var baseline = await fixture
      .Db.DispatchBaseRoutes.AsNoTracking()
      .SingleAsync();
    var connection = await fixture
      .Db.DispatchDeadheads.AsNoTracking()
      .SingleAsync();

    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );

    Assert.Equal(
      new[] { fixture.Current.Id, fixture.Future.Id },
      result.DispatchIds
    );
    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(18000, result.Route.Seconds);
    Assert.Equal(3, result.Stops.Count);
    Assert.Equal(
      new[]
      {
        fixture.Current.Stops[1].Id,
        fixture.Future.Stops[0].Id,
        fixture.Future.Stops[1].Id,
      },
      result.Itinerary.Select(stop => stop.Stop.Id)
    );
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(
      baseline.RouteJson,
      (
        await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()
      ).RouteJson
    );
    Assert.Equal(
      connection.RouteJson,
      (
        await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync()
      ).RouteJson
    );
  }

  [Theory]
  [InlineData("missing-base")]
  [InlineData("missing-connection")]
  [InlineData("wrong-base-endpoint")]
  [InlineData("wrong-connection-endpoint")]
  [InlineData("unverified-street")]
  [InlineData("off-route-current")]
  [InlineData("changed-profile")]
  public async Task MissingOrInvalidSavedInputsFailWithoutRepairOrProviderCalls(
    string scenario
  )
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    if (scenario == "missing-base")
      fixture.Db.DispatchBaseRoutes.Remove(
        await fixture.Db.DispatchBaseRoutes.SingleAsync()
      );
    if (scenario == "missing-connection")
      fixture.Db.DispatchDeadheads.Remove(
        await fixture.Db.DispatchDeadheads.SingleAsync()
      );
    if (scenario == "wrong-base-endpoint")
      (await fixture.Db.DispatchBaseRoutes.SingleAsync()).RouteJson =
        RoutePlanStorage.Serialize(SavedFuelHorizonFixture.Route(-78, -76));
    if (scenario == "wrong-connection-endpoint")
      (await fixture.Db.DispatchDeadheads.SingleAsync()).RouteJson =
        RoutePlanStorage.Serialize(SavedFuelHorizonFixture.Route(-79, -78.1));
    if (scenario == "unverified-street")
      fixture.Future.Stops[0].Address = "Unverified street";
    if (scenario == "changed-profile")
      fixture.State.Profile.HeightFeet += 1;
    var state =
      scenario == "off-route-current"
        ? fixture.State with
        {
          Progress = fixture.State.Progress! with { Position = new(41, -80) },
        }
        : fixture.State;
    await fixture.Db.SaveChangesAsync();
    var before = await fixture
      .Db.DispatchBaseRoutes.AsNoTracking()
      .Select(row => row.RouteJson)
      .ToArrayAsync();

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Horizon.BuildAsync(state, state.Profile, default)
    );

    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(
      before,
      await fixture
        .Db.DispatchBaseRoutes.AsNoTracking()
        .Select(row => row.RouteJson)
        .ToArrayAsync()
    );
    Assert.DoesNotContain(
      fixture.Db.ChangeTracker.Entries(),
      entry => entry.State is EntityState.Modified or EntityState.Added
    );
  }

  [Fact]
  public async Task CompletedFuturePickupReusesItsExistingPathWithoutAddingTheCompletedStop()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    fixture.Future.Stops[0].PickedUpAt = DateTime.UtcNow.AddMinutes(-1);
    await fixture.Db.SaveChangesAsync();

    var result = await fixture.Horizon.BuildAsync(
      fixture.State,
      fixture.State.Profile,
      default
    );

    Assert.Equal(300, result.Route.Miles);
    Assert.Equal(2, result.Route.Legs.Count);
    Assert.Equal(200, result.Route.Legs[1].Miles);
    Assert.DoesNotContain(
      result.Stops,
      stop => stop.Id == fixture.Future.Stops[0].Id
    );
    Assert.Contains(
      result.Route.Legs[1].Points,
      point => point.Longitude == -78
    );
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CancelledSavedHorizonNeverEntersAProvider()
  {
    await using var fixture = await SavedFuelHorizonFixture.CreateAsync();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        fixture.Horizon.BuildAsync(
          fixture.State,
          fixture.State.Profile,
          cancellation.Token
        )
    );

    Assert.Equal(0, fixture.Router.Calls);
  }
}
