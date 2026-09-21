using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using StoredFuel = Domain.Entities.Fuel.TruckFuelPlan;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelSavedRoadValidityTests
{
  [Theory]
  [InlineData("base")]
  [InlineData("base-delete")]
  [InlineData("connection")]
  [InlineData("connection-clear")]
  [InlineData("root-version")]
  [InlineData("root-owner")]
  [InlineData("root-hash")]
  [InlineData("root-delete")]
  [InlineData("native-revision")]
  public async Task LaterChangesInvalidateWarmDisplayWithoutReplacingThePlan(
    string change
  )
  {
    var columns = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: columns
    );
    if (change == "native-revision")
      await f.ReceiveCurrentAsync();
    var profile = await f.PrepareCalculationAsync();
    var root = f.State.Plan!;
    await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile)
      {
        ExecutionLegId = root.ExecutionLegId,
        AssignmentRevision = root.AssignmentRevision,
      },
      default
    );
    var load = await f.Services.Routes.LoadAsync(
      f.Current.Id,
      default,
      root.ExecutionLegId
    );
    var state = await f.Services.Routes.GetAsync(load, default);
    await f.Services.FuelPlans.ApplyAsync(state, default);
    Assert.False(
      state.Plan!.FuelPlan!.NeedsRefresh,
      string.Join("; ", state.Plan.FuelPlan.RefreshReasons)
    );
    var before = await StoredAsync(f);
    var saved = await f.Services.FuelPlans.ReadAsync(root.TruckId, default);
    Assert.NotNull(saved!.RoadDependencies);
    Assert.All(
      saved.RoadDependencies.Roads,
      road => Assert.Null(road.ProgressSignature)
    );
    f.Db.ChangeTracker.Clear();
    if (change.StartsWith("base", StringComparison.Ordinal))
    {
      if (change == "base-delete")
        await f.Db.DispatchBaseRoutes.ExecuteDeleteAsync();
      else
        await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
        );
    }
    else if (change == "connection")
      await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.InputHash, "corrected road")
      );
    else if (change == "connection-clear")
      await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.RouteJson, (string?)null)
      );
    else if (change == "root-delete")
      await f.Db.DispatchRoutePlans.ExecuteDeleteAsync();
    else if (change == "root-owner")
    {
      var other = new Truck { Id = Guid.NewGuid() };
      f.Db.Trucks.Add(other);
      await f.Db.SaveChangesAsync();
      await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.TruckId, other.Id)
      );
    }
    else if (change == "root-hash")
      await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.InputHash, "corrected root")
      );
    else if (change == "native-revision")
      await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.AssignmentRevision, root.AssignmentRevision + 1)
      );
    else
      await ChangePlanAsync(f, plan => plan.Version++);
    columns.Columns.Clear();

    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(state.Plan.FuelPlan!.NeedsRefresh);
    Assert.Null(state.Plan.FuelPlan.ScheduleImpact);
    Assert.Contains(
      state.Plan.FuelPlan.RefreshReasons,
      reason => reason.Contains("Saved fuel roads", StringComparison.Ordinal)
    );
    Assert.DoesNotContain(
      columns.Columns.SelectMany(x => x),
      name => name is "RouteJson" or "PlanJson" or "CheckedRouteJson"
    );
    Assert.Equal(before, await StoredAsync(f));
    Assert.False(saved.Plan.NeedsRefresh);
    Assert.Equal(0, f.Router.Calls);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData("tracking")]
  [InlineData("fuel-only")]
  [InlineData("unused-base")]
  public async Task ProgressAndUnconsumedRoadsDoNotInvalidateFuel(string change)
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);
    var saved = await f.Services.FuelPlans.ReadUncachedAsync(
      state.Plan!.TruckId,
      default
    );
    if (change == "tracking")
      await ChangePlanAsync(
        f,
        plan =>
        {
          plan.Tracking.OffRouteSince = DateTime.UtcNow;
          plan.Tracking.VisitedStops[f.Current.Stops[0].Id] = DateTime.UtcNow;
          plan.Tracking.PassedStopIds.Add(f.Current.Stops[0].Id);
        }
      );
    else if (change == "fuel-only")
      await ChangePlanAsync(
        f,
        plan => plan.FuelPlan!.Notes.Add("Fuel annotation.")
      );
    else
    {
      f.Db.DispatchBaseRoutes.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Current.Id,
          InputHash = "unconsumed root base",
          RouteJson = "{}",
        }
      );
      await f.Db.SaveChangesAsync();
    }

    Assert.True(
      await f.Services.Roads.MatchesAsync(
        FuelRoadDependencies.Remaining(saved!, state.Plan)!,
        default
      )
    );
    await f.Services.FuelPlans.ApplyAsync(state, default);
    Assert.False(
      state.Plan.FuelPlan!.NeedsRefresh,
      string.Join("; ", state.Plan.FuelPlan.RefreshReasons)
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task MissingLegacyEvidenceRetainsThePlanButRequiresRefresh()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var stored = await f.Db.Set<StoredFuel>().SingleAsync();
    var json = JsonNode.Parse(stored.SummaryJson)!;
    json.AsObject().Remove("roadDependencies");
    stored.SummaryJson = json.ToJsonString();
    await f.Db.SaveChangesAsync();
    var before = stored.SummaryJson;
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);

    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(state.Plan!.FuelPlan!.NeedsRefresh);
    Assert.Contains(
      state.Plan.FuelPlan.RefreshReasons,
      reason => reason.Contains("Saved fuel roads", StringComparison.Ordinal)
    );
    Assert.Equal(before, await StoredAsync(f));
  }

  [Fact]
  public async Task NewCalculationReplacesOldDependenciesAndRestoresValidity()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    await ChangePlanAsync(f, plan => plan.Version++);
    f.Services.Reads.Invalidate(RoutePlanStore.CacheKey(f.Current.Id, null));
    f.Db.ChangeTracker.Clear();

    var updated = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);
    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(updated.Plan!.CalculatedAt > initial.Plan!.CalculatedAt);
    Assert.False(
      state.Plan!.FuelPlan!.NeedsRefresh,
      string.Join("; ", state.Plan.FuelPlan.RefreshReasons)
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FallbackReplacementOrNewBaseInvalidatesSavedFuel(
    bool baseAppears
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    var basis = await f.UseFutureFallbackAsync(profile);
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);
    await f.Services.FuelPlans.ApplyAsync(state, default);
    Assert.False(state.Plan!.FuelPlan!.NeedsRefresh);
    f.Db.ChangeTracker.Clear();
    if (baseAppears)
    {
      f.Db.DispatchBaseRoutes.Add(basis);
      await f.Db.SaveChangesAsync();
    }
    else
      await f
        .Db.DispatchRoutePlans.Where(x => x.DispatchId == f.Future.Id)
        .ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.InputHash, "new fallback")
        );

    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(state.Plan.FuelPlan!.NeedsRefresh);
    Assert.Empty(state.Plan.FuelPlan.StopArrivals);
    Assert.Contains(
      state.Plan.FuelPlan.RefreshReasons,
      reason => reason.Contains("Saved fuel roads", StringComparison.Ordinal)
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task CancelledValidationDoesNotReadRoads()
  {
    var columns = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: columns
    );
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    columns.Columns.Clear();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => f.Services.Roads.MatchesAsync([], cancellation.Token)
    );

    Assert.Empty(columns.Columns);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  private static Task<string> StoredAsync(SavedFuelHorizonFixture f) =>
    f
      .Db.Set<StoredFuel>()
      .AsNoTracking()
      .Select(x => x.SummaryJson)
      .SingleAsync();

  private static async Task ChangePlanAsync(
    SavedFuelHorizonFixture f,
    Action<RoutePlan> change
  )
  {
    var json = await f
      .Db.DispatchRoutePlans.Select(x => x.PlanJson)
      .SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      json,
      RoutingJson.Options
    )!;
    change(plan);
    var updated = RoutePlanStorage.Serialize(plan);
    await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.PlanJson, updated)
    );
  }
}
