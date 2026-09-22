using System.Text.Json;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fuel;
using Domain.Models.Fuel;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task FuelEditorRejectsUnconfirmedStationCountryWithoutSaving()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;
    fixture.Stations[0] = fixture.Stations[0] with { Country = "CA" };
    fixture.Services.Reads.Invalidate("fuel");
    var request = new FuelPlanEditRequest(
      initial.ExpectedCalculatedAt,
      [FullTankEdit(fixture)]
    );

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      request,
      false,
      default
    );

    Assert.False(preview.ValuesAvailable);
    Assert.Contains("border", Assert.Single(preview.Errors));
    Assert.Single(preview.Stops);
    Assert.Null(preview.Stops[0].PurchaseLimitGallons);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Fuel.EditAsync(fixture.Load.Id, request, true, default)
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task PreviewReplacesClientHeadroomAndUnresolvedVisitsNeverEchoIt()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var supplied = FullTankEdit(fixture) with { PurchaseLimitGallons = 999 };
    var request = new FuelPlanEditRequest(
      initial.ExpectedCalculatedAt,
      [supplied]
    );
    var rows = await FuelRowsAsync(fixture);
    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      request,
      false,
      default
    );

    var limit = Assert.Single(preview.Stops).PurchaseLimitGallons;
    Assert.NotNull(limit);
    Assert.Equal(Assert.Single(preview.Plan.Stops).BuyGallons, limit.Value, 8);
    Assert.NotEqual(999, limit.Value);
    Assert.Equal(999, supplied.PurchaseLimitGallons);
    using (
      var json = JsonDocument.Parse(
        JsonSerializer.Serialize(
          preview,
          new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )
      )
    )
      Assert.Equal(
        limit.Value,
        json.RootElement.GetProperty("stops")[0]
          .GetProperty("purchaseLimitGallons")
          .GetDouble()
      );

    fixture.Stations[0] = fixture.Stations[0] with { Latitude = 45 };
    fixture.Services.Reads.Invalidate("fuel");
    var unresolved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      request,
      false,
      default
    );
    Assert.False(unresolved.ValuesAvailable);
    Assert.Null(Assert.Single(unresolved.Stops).PurchaseLimitGallons);
    Assert.Equal(999, supplied.PurchaseLimitGallons);
    Assert.Equal(rows, await FuelRowsAsync(fixture));
  }

  [Fact]
  public async Task FuelEditPreviewReplaysWithoutChangingEitherSavedCopyOrCallingRouting()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
      false,
      default
    );

    Assert.Empty(preview.Errors);
    var segment = Assert.Single(
      Assert.IsType<List<FuelPlanEditSegment>>(preview.Segments)
    );
    Assert.Null(segment.AfterStop);
    Assert.Equal(fixture.Load.Stops[^1].Id, segment.BeforeStop.Id);
    Assert.Equal(segment.BeforeStop.Id, segment.BeforeStopId);
    Assert.Equal(fixture.Load.Id, segment.DispatchId);
    Assert.Equal(initial.Segments, preview.Segments);
    Assert.True(preview.Plan.ManuallyEdited);
    var purchase = Assert.Single(preview.Plan.Stops);
    Assert.Equal(preview.FillLimitGallons, purchase.DepartureGallons, 8);
    Assert.Equal(
      purchase.DepartureGallons - purchase.ArrivalGallons,
      purchase.BuyGallons,
      8
    );
    Assert.Equal(
      purchase.BuyGallons,
      Assert.Single(preview.Stops).PurchaseLimitGallons!.Value,
      8
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task ManualFuelSaveUpdatesSingleTruckSnapshotAndCompatibilityCopyTogether()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var calls = fixture.Router.Calls;
    var route = (await fixture.Plans.GetAsync(fixture.Load.Id, default))
      .Plan!
      .Route;

    var result = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
      true,
      default
    );

    var snapshot = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var compatibility = RoutePlanStorage.Read(
      (
        await RoutePlanStorage.LoadAsync(
          fixture.Db,
          await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync(),
          default
        )
      )!
    )!;
    Assert.True(snapshot.Plan.ManuallyEdited);
    Assert.True(compatibility.FuelPlan!.ManuallyEdited);
    Assert.Equal(result.ExpectedCalculatedAt, snapshot.CalculatedAt);
    Assert.Equal(snapshot.CalculatedAt, compatibility.FuelPlan.CalculatedAt);
    Assert.Equal(
      result.Plan.Stops[0].BuyGallons,
      snapshot.Plan.Stops[0].BuyGallons,
      8
    );
    Assert.Equal(
      snapshot.Plan.Stops[0].BuyGallons,
      compatibility.FuelPlan.Stops[0].BuyGallons,
      8
    );
    Assert.True(snapshot.Plan.Stops[0].FillToTarget);
    Assert.Equal(1, await fixture.Db.Set<TruckFuelPlan>().CountAsync());
    Assert.Equal(
      JsonSerializer.Serialize(route),
      JsonSerializer.Serialize(compatibility.Route)
    );
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task StaleFuelEditCannotReplaceACommittedManualSave()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
      true,
      default
    );
    var rows = await FuelRowsAsync(fixture);

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
          true,
          default
        )
    );

    Assert.Equal(rows, await FuelRowsAsync(fixture));
  }

  [Fact]
  public async Task FuelEditConflictAfterPreviewInputsRollsBackItsCompatibilityWrite()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var rows = await FuelRowsAsync(fixture);
    TruckFuelPlanSnapshot? winner = null;
    fixture.Sender.BeforeFuel = async _ =>
    {
      fixture.Sender.BeforeFuel = null;
      await using var competingContext = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(fixture.Connection)
          .Options
      );
      var store = new TruckFuelPlanStore(
        competingContext,
        NullLogger<TruckFuelPlanStore>.Instance
      );
      var previous = Assert.IsType<TruckFuelPlanSnapshot>(
        await store.ReadAsync(fixture.Truck.Id, true, default)
      );
      var timestamp = previous.CalculatedAt.AddMilliseconds(1);
      previous.Plan.CalculatedAt = timestamp;
      previous.Plan.ManuallyEdited = true;
      previous.Plan.Notes = ["Competing editor"];
      winner = previous with { CalculatedAt = timestamp };
      Assert.True(
        await store.ReplaceAsync(winner, initial.ExpectedCalculatedAt, default)
      );
    };

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
          true,
          default
        )
    );

    var after = await FuelRowsAsync(fixture);
    Assert.NotNull(winner);
    Assert.Equal(rows.Route, after.Route);
    Assert.Equal(
      winner.CalculatedAt.Ticks / 10,
      after.CalculatedAt!.Value.Ticks / 10
    );
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    Assert.Equal(winner.Plan.Notes, saved.Plan.Notes);
    Assert.True(saved.Plan.ManuallyEdited);
  }

  [Fact]
  public async Task AutomaticFuelCalculationProtectsManualPlanUntilExplicitReset()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var edited = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, [FullTankEdit(fixture)]),
      true,
      default
    );
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;
    var priceCalls = fixture.Sender.FuelCalls;

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default)
    );
    Assert.Contains(
      "manual",
      error.Message,
      StringComparison.OrdinalIgnoreCase
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(priceCalls, fixture.Sender.FuelCalls);

    var automatic = await fixture.Services.Fuel.ResetAsync(
      fixture.Load.Id,
      edited.ExpectedCalculatedAt,
      default
    );
    Assert.False(automatic.Plan!.ManuallyEdited);
    Assert.True(automatic.Plan!.CalculatedAt > edited.ExpectedCalculatedAt);
    Assert.False(
      (
        await fixture.Services.FuelPlans.ReadCheckedAsync(
          fixture.Truck.Id,
          default
        )
      )!
        .Plan
        .ManuallyEdited
    );
    Assert.Equal(1, await fixture.Db.Set<TruckFuelPlan>().CountAsync());
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task ExplicitResetReplacesPlanFromPreviousExecution()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var previousLeg = Guid.NewGuid();
    var previousRevision = 7L;
    var previousCalculatedAt = saved.CalculatedAt.AddTicks(10);
    saved.Plan.ExecutionLegId = previousLeg;
    saved.Plan.AssignmentRevision = previousRevision;
    saved.Plan.CalculatedAt = previousCalculatedAt;
    var previous = saved with
    {
      CalculatedAt = previousCalculatedAt,
      RootExecutionLegId = previousLeg,
      AssignmentRevision = previousRevision,
      RoadDependencies = FuelRoadDependencies.Capture(
        saved
          .RoadDependencies!.Roads.Select(road =>
            road with
            {
              Work = new(road.Work.DispatchId, previousLeg),
            }
          )
          .ToArray()
      ),
      Stops = saved
        .Stops.Select(stop =>
          stop with
          {
            ExecutionLegId = previousLeg,
            AssignmentRevision = previousRevision,
          }
        )
        .ToList(),
    };
    Assert.True(await fixture.Services.FuelPlans.SaveAsync(previous, default));

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        fixture.Services.Fuel.ResetAsync(
          fixture.Load.Id,
          saved.CalculatedAt,
          default
        )
    );
    Assert.Equal(
      previousCalculatedAt,
      (
        await fixture.Services.FuelPlans.ReadCheckedAsync(
          fixture.Truck.Id,
          default
        )
      )!.CalculatedAt
    );

    var automatic = await fixture.Services.Fuel.ResetAsync(
      fixture.Load.Id,
      null,
      default
    );

    Assert.Null(automatic.Plan!.ExecutionLegId);
    Assert.Equal(0, automatic.Plan!.AssignmentRevision);
    Assert.True(automatic.Plan!.CalculatedAt > previousCalculatedAt);
    var current = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    Assert.Null(current.RootExecutionLegId);
    Assert.Equal(fixture.Load.Id, current.RootDispatchId);
  }

  [Fact]
  public async Task OverCapacityFuelDraftReportsErrorAndCannotReplaceExistingPlan()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var rows = await FuelRowsAsync(fixture);
    var draft = new FuelPlanEditRequest(
      initial.ExpectedCalculatedAt,
      [new(fixture.Stations[0].Id, fixture.Load.Stops[^1].Id, 500, false)]
    );

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      draft,
      false,
      default
    );
    Assert.Contains(
      preview.Errors,
      x => x.Contains("fill limit", StringComparison.OrdinalIgnoreCase)
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Fuel.EditAsync(fixture.Load.Id, draft, true, default)
    );

    Assert.Equal(rows, await FuelRowsAsync(fixture));
  }

  [Theory]
  [InlineData("unpriced")]
  [InlineData("off-route")]
  [InlineData("removed")]
  public async Task UnresolvedSavedFuelVisitRemainsEditableAndCanBeRemoved(
    string stationChange
  )
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var original = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var savedPurchase = Assert.Single(original.Plan.Stops);
    var station = fixture.Stations[0];
    fixture.Stations.Add(
      station with
      {
        Id = Guid.NewGuid(),
        ExternalId = "alternative",
        Name = "Alternative fuel",
      }
    );
    if (stationChange == "unpriced")
      fixture.Stations[0] = station with
      {
        Discounts = [],
        CashDiscount = null,
        IftaDiscount = null,
      };
    if (stationChange == "off-route")
      fixture.Stations[0] = station with { Latitude = 45 };
    if (stationChange == "removed")
      fixture.Stations.RemoveAt(0);
    fixture.Services.Reads.Invalidate("fuel");
    fixture.Location.FuelPercent = 95;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;

    var unresolved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );

    Assert.NotEmpty(unresolved.Errors);
    Assert.Contains(savedPurchase.Name, unresolved.Errors[0]);
    Assert.Contains(
      stationChange == "off-route"
        ? "not within 40 miles of the remaining route"
        : "no current price is available",
      unresolved.Errors[0]
    );
    var unresolvedSegment = Assert.Single(
      Assert.IsType<List<FuelPlanEditSegment>>(unresolved.Segments)
    );
    Assert.Null(unresolvedSegment.AfterStop);
    Assert.Equal(fixture.Load.Stops[^1].Id, unresolvedSegment.BeforeStopId);
    Assert.False(unresolved.ValuesAvailable);
    Assert.True(unresolved.Plan.NeedsRefresh);
    Assert.Equal(original.CalculatedAt, unresolved.ExpectedCalculatedAt);
    var edit = Assert.Single(unresolved.Stops);
    Assert.Null(edit.PurchaseLimitGallons);
    var displayed = Assert.Single(unresolved.Plan.Stops);
    Assert.Equal(savedPurchase.StationId, edit.StationId);
    Assert.Equal(savedPurchase.BeforeStopId, edit.BeforeStopId);
    Assert.Equal(savedPurchase.StationId, displayed.StationId);
    Assert.Equal(savedPurchase.Name, displayed.Name);
    Assert.Equal(savedPurchase.Address, displayed.Address);
    Assert.Equal(savedPurchase.BeforeStopId, displayed.BeforeStopId);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          new(unresolved.ExpectedCalculatedAt, unresolved.Stops),
          true,
          default
        )
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));

    var cleaned = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(unresolved.ExpectedCalculatedAt, []),
      false,
      default
    );
    Assert.Empty(cleaned.Errors);
    Assert.True(cleaned.ValuesAvailable);
    Assert.Equal(unresolved.Segments, cleaned.Segments);
    Assert.Empty(cleaned.Stops);
    Assert.Empty(cleaned.Plan.Stops);
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task ObsoleteSavedFuelLegCannotMistakeCurrentGpsForAPassedPurchase()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var store = new TruckFuelPlanStore(
      fixture.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    var original = Assert.IsType<TruckFuelPlanSnapshot>(
      await store.ReadAsync(fixture.Truck.Id, true, default)
    );
    var timestamp = original.CalculatedAt.AddMilliseconds(1);
    original.Plan.CalculatedAt = timestamp;
    original.BaselineRoute!.Legs[0].Points[0] = new(40, -85);
    var obsolete = original with { CalculatedAt = timestamp };
    Assert.True(
      await store.ReplaceAsync(obsolete, original.CalculatedAt, default)
    );
    var purchase = Assert.Single(obsolete.Plan.Stops);
    var state = await fixture.Plans.GetAsync(fixture.Load.Id, default);
    var oldProjection = new RouteGeometry(obsolete.BaselineRoute).Match(
      state.Progress!.Position!
    );
    Assert.True(oldProjection.Along > purchase.RouteMilesAhead);
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );

    Assert.Equal(purchase.StationId, Assert.Single(preview.Stops).StationId);
    Assert.Equal(
      purchase.StationId,
      Assert.Single(preview.Plan.Stops).StationId
    );
    Assert.Empty(preview.Errors);
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task MissingAllPricesLeavesSavedFuelRowsEditableWithoutInventingValidBalances()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    fixture.Stations.Clear();
    fixture.Services.Reads.Invalidate("fuel");
    var rows = await FuelRowsAsync(fixture);
    var calls = fixture.Router.Calls;

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );

    Assert.NotEmpty(preview.Errors);
    Assert.Contains(saved.Plan.Stops[0].Name, preview.Errors[0]);
    Assert.NotEmpty(Assert.IsType<List<FuelPlanEditSegment>>(preview.Segments));
    Assert.False(preview.ValuesAvailable);
    Assert.Equal(
      saved.Plan.Stops[0].StationId,
      Assert.Single(preview.Stops).StationId
    );
    Assert.Null(Assert.Single(preview.Stops).PurchaseLimitGallons);
    Assert.Equal(
      saved.Plan.Stops[0].StationId,
      Assert.Single(preview.Plan.Stops).StationId
    );
    var removed = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(preview.ExpectedCalculatedAt, []),
      false,
      default
    );
    Assert.Empty(removed.Stops);
    Assert.Empty(removed.Plan.Stops);
    Assert.True(removed.ValuesAvailable);
    Assert.True(removed.Plan.NeedsRefresh);
    Assert.Contains(
      removed.Errors,
      error => error.Contains("final arrival", StringComparison.Ordinal)
    );
    Assert.Equal(100, removed.Plan.StartingGallons);
    Assert.Equal(50, removed.Plan.RemainingMiles, 8);
    Assert.Equal(0, removed.Plan.PurchaseGallons);
    Assert.Equal(0, removed.Plan.ExpectedFutureFuelCostUsd);
    var profile = await fixture.Plans.ProfileAsync(fixture.Truck.Id, default);
    Assert.Equal(100 - 50 / profile.Mpg!.Value, removed.Plan.ArrivalGallons, 8);
    Assert.InRange(removed.Plan.ArrivalGallons, 92, 93);
    Assert.True(removed.Plan.ArrivalGallons < profile.TankGallons / 2);
    Assert.Contains(
      removed.Plan.Notes,
      note => note.Contains("coverage is unavailable", StringComparison.Ordinal)
    );
    Assert.Equal(preview.Segments, removed.Segments);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          new(preview.ExpectedCalculatedAt, []),
          true,
          default
        )
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  [Fact]
  public async Task GpsOutsideObsoleteFuelLegToleranceDoesNotSilentlyDeleteAPlannedFuelVisit()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    var saved = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var timestamp = saved.CalculatedAt.AddMilliseconds(1);
    saved.Plan.CalculatedAt = timestamp;
    saved.BaselineRoute!.Legs[0].Points[0] = new(40.2, -79.5);
    saved.Plan.Stops[0].RouteMilesAhead = 1;
    saved.Plan.Stops[0].MilesAhead = 1;
    Assert.True(
      await new TruckFuelPlanStore(
        fixture.Db,
        NullLogger<TruckFuelPlanStore>.Instance
      ).ReplaceAsync(
        saved with
        {
          CalculatedAt = timestamp,
        },
        saved.CalculatedAt,
        default
      )
    );
    var state = await fixture.Plans.GetAsync(fixture.Load.Id, default);
    var projection = new RouteGeometry(saved.BaselineRoute!).Match(
      state.Progress!.Position!
    );
    Assert.True(projection.Away > 2);
    Assert.True(projection.Along > saved.Plan.Stops[0].RouteMilesAhead);
    var rows = await FuelRowsAsync(fixture);

    var preview = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );

    Assert.Equal(
      saved.Plan.Stops[0].StationId,
      Assert.Single(preview.Stops).StationId
    );
    Assert.Equal(
      saved.Plan.Stops[0].StationId,
      Assert.Single(preview.Plan.Stops).StationId
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
  }

  [Fact]
  public async Task RemovingAllFuelStopsKeepsAnExplicitManualEmptyPlanWhenFuelIsSufficient()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    fixture.Location.FuelPercent = 95;
    fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );

    var saved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, []),
      true,
      default
    );

    Assert.Empty(saved.Errors);
    Assert.Empty(saved.Plan.Stops);
    Assert.True(saved.Plan.ManuallyEdited);
    Assert.Empty(
      (
        await fixture.Services.FuelPlans.ReadCheckedAsync(
          fixture.Truck.Id,
          default
        )
      )!
        .Plan
        .Stops
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default)
    );
  }

  [Fact]
  public async Task ManualFuelVisitsCoverCurrentAndFutureLoadsInRouteOrderWithoutRoutingRequests()
  {
    var now = DateTime.UtcNow;
    var date = DateOnly.FromDateTime(now);
    var priceDate = FuelPricingDate.FromUtc(now);
    FuelStationDto Station(string name, decimal longitude) =>
      new(
        Guid.NewGuid(),
        name,
        name,
        "Street",
        "City",
        "NY",
        "",
        "US",
        40,
        longitude,
        [new("USD", "Diesel", 4, 4, 0, priceDate, priceDate, 4, "US gal")]
      );
    var stations = new List<FuelStationDto>
    {
      Station("Current stop", -79.2m),
      Station("Future stop", -78.1m),
    };
    await using var fixture = await Fixture.CreateAsync(
      stations,
      pickedUp: true
    );
    fixture.Location.FuelPercent = 40;
    fixture.Location.FuelUpdatedAt = now;
    fixture.DateCurrentLoad(date);
    var next = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      LoadNumber = 124,
      Status = "assigned",
      TruckId = fixture.Truck.Id,
      ShipDate = date.AddDays(1),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          Latitude = 40,
          Longitude = -78.8m,
          ScheduledDate = date.AddDays(1),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          Latitude = 40,
          Longitude = -78,
        },
      ],
    };
    fixture.Db.Dispatches.Add(next);
    await fixture.Db.SaveChangesAsync();
    await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    await fixture.PrepareFuelUpcomingAsync(next.Id);
    var calls = fixture.Router.Calls;
    var edits = new List<FuelPlanEditStop>
    {
      new(stations[0].Id, fixture.Load.Stops[^1].Id, 30, false),
      new(stations[1].Id, next.Stops[^1].Id, 0, true),
    };

    var saved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, edits),
      true,
      default
    );

    Assert.Empty(saved.Errors);
    var segments = Assert.IsType<List<FuelPlanEditSegment>>(saved.Segments);
    Assert.Equal(
      [fixture.Load.Stops[^1].Id, next.Stops[0].Id, next.Stops[^1].Id],
      segments.Select(x => x.BeforeStopId)
    );
    Assert.Equal(
      [fixture.Load.Id, next.Id, next.Id],
      segments.Select(x => x.DispatchId)
    );
    Assert.Null(segments[0].AfterStop);
    Assert.Equal(fixture.Load.Stops[^1].Id, segments[1].AfterStop!.Id);
    Assert.Equal(next.Stops[0].Id, segments[2].AfterStop!.Id);
    Assert.All(segments, x => Assert.Equal(x.BeforeStopId, x.BeforeStop.Id));
    Assert.Equal("Pick Up", segments[1].BeforeStop.Job);
    Assert.Equal("Drop Off", segments[2].BeforeStop.Job);
    Assert.Equal([fixture.Load.Id, next.Id], saved.Plan.DispatchIds);
    Assert.Equal(
      [fixture.Load.Id, next.Id],
      saved.Plan.Stops.Select(x => x.DispatchId)
    );
    Assert.Equal([1, 2], saved.Plan.Stops.Select(x => x.Number));
    Assert.True(
      saved.Plan.Stops[0].MilesAhead < saved.Plan.Stops[1].MilesAhead
    );
    Assert.Equal(30, saved.Plan.Stops[0].BuyGallons);
    Assert.Equal(
      saved.FillLimitGallons,
      saved.Plan.Stops[1].DepartureGallons,
      8
    );
    Assert.Equal(calls, fixture.Router.Calls);
    var rows = await FuelRowsAsync(fixture);
    var reversedRequest = new FuelPlanEditRequest(
      saved.ExpectedCalculatedAt,
      edits.AsEnumerable().Reverse().ToList()
    );
    var reversed = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      reversedRequest,
      false,
      default
    );
    Assert.NotEmpty(reversed.Errors);
    Assert.Equal(segments, reversed.Segments);
    Assert.False(reversed.ValuesAvailable);
    Assert.Equal(
      edits.AsEnumerable().Reverse().Select(x => x.StationId),
      reversed.Stops.Select(x => x.StationId)
    );
    Assert.Equal(2, reversed.Plan.Stops.Count);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          reversedRequest,
          true,
          default
        )
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));

    var misplaced = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(
        saved.ExpectedCalculatedAt,
        [new(stations[1].Id, fixture.Load.Stops[^1].Id, 0, true)]
      ),
      false,
      default
    );
    Assert.Contains(
      "Future stop is not near this part of the trip",
      Assert.Single(misplaced.Errors)
    );
    Assert.Equal(segments, misplaced.Segments);

    var corrected = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(
        saved.ExpectedCalculatedAt,
        [new(stations[1].Id, segments[^1].BeforeStopId, 0, true)]
      ),
      false,
      default
    );
    Assert.Empty(corrected.Errors);
    Assert.Equal(
      next.Stops[^1].Id,
      Assert.Single(corrected.Stops).BeforeStopId
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    Assert.Equal(calls, fixture.Router.Calls);
  }

  private static async Task<Fixture> CreateFuelEditingFixtureAsync()
  {
    var fixture = await Fixture.CreateAsync(pickedUp: true);
    try
    {
      fixture.Location.FuelPercent = 40;
      fixture.Location.FuelUpdatedAt = DateTime.UtcNow;
      await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
      await fixture.Service.RecalculateFuelAsync(fixture.Load.Id, default);
      return fixture;
    }
    catch
    {
      await fixture.DisposeAsync();
      throw;
    }
  }

  private static FuelPlanEditStop FullTankEdit(Fixture fixture) =>
    new(fixture.Stations[0].Id, fixture.Load.Stops[^1].Id, 0, true);

  private static async Task<FuelSavedRows> FuelRowsAsync(Fixture fixture)
  {
    var route = (
      await fixture
        .Db.DispatchRoutePlans.AsNoTracking()
        .SingleAsync(x => x.DispatchId == fixture.Load.Id)
    ).PlanJson;
    var fuel = await fixture
      .Db.Set<TruckFuelPlan>()
      .AsNoTracking()
      .SingleOrDefaultAsync();
    return new(
      route,
      fuel?.CalculatedAt,
      fuel?.SummaryJson,
      fuel?.CheckedRouteJson
    );
  }

  private sealed record FuelSavedRows(
    string Route,
    DateTime? CalculatedAt,
    string? Summary,
    string? Geometry
  );
}
