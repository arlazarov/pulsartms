using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Tests.Support;
using StoredFuel = Domain.Entities.Fuel.TruckFuelPlan;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelSavedRoadPublicationTests
{
  [Theory]
  [InlineData("automatic", "base")]
  [InlineData("manual", "base")]
  [InlineData("reset", "base")]
  [InlineData("automatic", "connection")]
  [InlineData("manual", "connection")]
  [InlineData("reset", "connection")]
  [InlineData("automatic", "root-tracking")]
  [InlineData("manual", "root-tracking")]
  [InlineData("reset", "root-tracking")]
  [InlineData("automatic", "base-delete")]
  [InlineData("manual", "connection-clear")]
  [InlineData("automatic", "root-version")]
  [InlineData("manual", "root-hash")]
  public async Task LateRoadChangesKeepProfileAndBothFuelCopies(
    string operation,
    string change
  )
  {
    var columns = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: columns
    );
    var profile = await f.PrepareCalculationAsync();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    var before = await SnapshotAsync(f);
    var cached = await f.Services.FuelPlans.ReadAsync(
      f.State.Plan!.TruckId,
      default
    );
    var generation = f.Services.Reads.Generation(
      $"truck-fuel:{f.State.Plan.TruckId}"
    );
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await ChangeAsync(f, change);
      columns.Columns.Clear();
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (operation == "manual")
        await f.Services.Fuel.EditAsync(
          f.Current.Id,
          new(initial.Plan!.CalculatedAt, []),
          true,
          default
        );
      else if (operation == "reset")
        await f.Services.Fuel.ResetAsync(
          f.Current.Id,
          initial.Plan!.CalculatedAt,
          default
        );
      else
        await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    });

    Assert.Contains("Saved roads changed", error.Message);
    Assert.DoesNotContain(
      columns.Columns.SelectMany(x => x),
      name => name is "RouteJson" or "PlanJson"
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(before, await SnapshotAsync(f));
    Assert.Equal(
      generation,
      f.Services.Reads.Generation($"truck-fuel:{f.State.Plan.TruckId}")
    );
    Assert.Equal(
      cached!.CalculatedAt,
      (
        await f.Services.FuelPlans.ReadAsync(f.State.Plan.TruckId, default)
      )!.CalculatedAt
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData("plan-revision")]
  [InlineData("row-revision")]
  [InlineData("row-dispatch")]
  public async Task NativeRootKeepsItsExecutionIdentityThroughPublication(
    string change
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var leg = await f.ReceiveCurrentAsync();
    var profile = await f.PrepareCalculationAsync();
    var request = new FuelBuildRequest(profile)
    {
      ExecutionLegId = leg.Id,
      AssignmentRevision = leg.Revision,
    };
    await f.Services.Fuel.BuildAsync(f.Current.Id, request, default);
    var before = await SnapshotAsync(f);
    f.Publication.BeforeBegin = async () =>
    {
      if (change == "row-revision")
        await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.AssignmentRevision, leg.Revision + 1)
        );
      else if (change == "row-dispatch")
        await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.DispatchId, f.Future.Id)
        );
      else
        await ChangePlanAsync(
          f,
          f.Current.Id,
          plan => plan.AssignmentRevision++
        );
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Fuel.BuildAsync(f.Current.Id, request, default)
    );

    Assert.Contains("Saved roads changed", error.Message);
    Assert.Equal(before, await SnapshotAsync(f));
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task StaleCachedCurrentRoadCannotPublishFuel()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    await f.Services.Routes.GetAsync(f.Current.Id, default);
    var before = await SnapshotAsync(f);
    await ChangeAsync(f, "root-version");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default)
    );

    Assert.Equal(before, await SnapshotAsync(f));
    f.Services.Reads.Invalidate(RoutePlanStore.CacheKey(f.Current.Id, null));
    f.Db.ChangeTracker.Clear();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    Assert.NotEqual(before, await SnapshotAsync(f));
  }

  [Theory]
  [InlineData("version")]
  [InlineData("ownership")]
  [InlineData("base-appears")]
  public async Task FuturePlanFallbackAndItsBaseSelectionRemainDependencies(
    string change
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    var basis = await f.UseFutureFallbackAsync(profile);
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var before = await SnapshotAsync(f);
    f.Publication.BeforeBegin = async () =>
    {
      if (change == "base-appears")
      {
        f.Db.DispatchBaseRoutes.Add(basis);
        await f.Db.SaveChangesAsync();
      }
      else
        await ChangePlanAsync(
          f,
          f.Future.Id,
          value =>
          {
            if (change == "version")
              value.Version++;
            else
              value.FromCurrentPosition = true;
          }
        );
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default)
    );

    Assert.Contains("Saved roads changed", error.Message);
    Assert.Equal(before, await SnapshotAsync(f));
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task OnwardConnectionRemainsProtectedWithoutEligibleExitPrices(
    bool clear
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    var regions = new FuelRegionPlanner(
      f.Services.FuelInputs,
      Options.Create(new FuelRegionOptions()),
      f.Services.Deadheads,
      new RouteRegionLookup()
    );
    var arrival = await regions.BuildAsync(
      f.State.Plan,
      f.State.Profile,
      [],
      0,
      default,
      suppliedInputs: work
    );
    Assert.NotNull(arrival.Road);
    Assert.Equal(SavedRoadKind.Connection, arrival.Road.Kind);
    Assert.Equal(f.Future.Id, arrival.Road.Work.DispatchId);
    await ChangeAsync(f, clear ? "connection-clear" : "connection");
    await using var transaction = await f.Services.Publication.BeginAsync(
      work.Itinerary,
      [arrival.History!],
      default
    );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => Validator(f).RequireCurrentAsync([arrival.Road], default)
    );

    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData("unchanged")]
  [InlineData("fuel-only")]
  [InlineData("unused-base")]
  [InlineData("tracking-order")]
  public async Task UnchangedRoadInputsStillPublish(string change)
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    if (change == "tracking-order")
      await ChangePlanAsync(
        f,
        f.Current.Id,
        plan =>
        {
          plan.Tracking.VisitedStops = f
            .Current.Stops.OrderByDescending(x => x.Id)
            .ToDictionary(x => x.Id, _ => DateTime.UtcNow.AddMinutes(-30));
        }
      );
    f.Db.ChangeTracker.Clear();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    f.Db.ChangeTracker.Clear();
    f.Publication.BeforeBegin = async () =>
    {
      if (change == "tracking-order")
        await ChangePlanAsync(
          f,
          f.Current.Id,
          plan =>
          {
            plan.Tracking.VisitedStops = plan
              .Tracking.VisitedStops.OrderBy(x => x.Key)
              .ToDictionary(x => x.Key, x => x.Value);
          }
        );
      else if (change == "fuel-only")
        await ChangePlanAsync(
          f,
          f.Current.Id,
          plan => plan.FuelPlan!.Notes.Add("Concurrent fuel annotation.")
        );
      else if (change == "unused-base")
      {
        f.Db.DispatchBaseRoutes.Add(
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = f.Current.Id,
            InputHash = "unused current baseline",
            RouteJson = "{}",
          }
        );
        await f.Db.SaveChangesAsync();
      }
    };

    var updated = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );

    Assert.True(updated.Plan!.CalculatedAt > initial.Plan!.CalculatedAt);
    Assert.Equal(
      updated.Plan!.CalculatedAt,
      (
        await f.Services.FuelPlans.ReadUncachedAsync(
          f.State.Plan!.TruckId,
          default
        )
      )!.CalculatedAt
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task RepeatedReadsOfOneRoadCannotDiscardTheEarlierObservation()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var first = await f.Horizon.BuildAsync(f.State, f.State.Profile, default);
    await ChangeAsync(f, "base");
    var second = await f.Horizon.BuildAsync(f.State, f.State.Profile, default);
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    await using var transaction = await f.Services.Publication.BeginAsync(
      work.Itinerary,
      default
    );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        Validator(f)
          .RequireCurrentAsync([.. first.Roads, .. second.Roads], default)
    );
  }

  [Fact]
  public async Task MetadataValidationRequiresTheOwningTransaction()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => Validator(f).RequireCurrentAsync([], default)
    );
  }

  private static SavedRoadValidation Validator(SavedFuelHorizonFixture f) =>
    f.Services.Roads;

  private static async Task<string[]> SnapshotAsync(SavedFuelHorizonFixture f)
  {
    var json = await f
      .Db.DispatchRoutePlans.AsNoTracking()
      .Where(x => x.Id == f.State.Plan!.Id)
      .Select(x => x.PlanJson)
      .SingleAsync();
    var root = JsonSerializer.Deserialize<RoutePlan>(
      json,
      RoutePlanningService.Json
    )!;
    var stored = await f.Db.Set<StoredFuel>().AsNoTracking().SingleAsync();
    return
    [
      JsonSerializer.Serialize(root.FuelPlan, RoutePlanningService.Json),
      stored.SummaryJson,
      stored.CheckedRouteJson ?? "",
      (
        await f.Db.TruckPlanningProfiles.AsNoTracking().SingleAsync()
      ).SettingsJson,
    ];
  }

  private static async Task ChangeAsync(
    SavedFuelHorizonFixture f,
    string change
  )
  {
    if (change == "base")
      await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
      );
    else if (change == "base-delete")
      await f.Db.DispatchBaseRoutes.ExecuteDeleteAsync();
    else if (change == "connection")
      await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
      );
    else if (change == "connection-clear")
      await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.RouteJson, (string?)null)
      );
    else if (change == "root-hash")
      await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.InputHash, "concurrent input revision")
      );
    else
      await ChangePlanAsync(
        f,
        f.Current.Id,
        plan =>
        {
          if (change == "root-version")
            plan.Version++;
          else
            plan.Tracking.PassedStopIds.Add(f.Current.Stops[^1].Id);
        }
      );
  }

  private static async Task ChangePlanAsync(
    SavedFuelHorizonFixture f,
    Guid dispatchId,
    Action<RoutePlan> change
  )
  {
    var rows = f.Db.DispatchRoutePlans.Where(x => x.DispatchId == dispatchId);
    var json = await rows.Select(x => x.PlanJson).SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      json,
      RoutePlanningService.Json
    )!;
    change(plan);
    var updated = RoutePlanStorage.Serialize(plan);
    await rows.ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanJson, updated));
  }
}
