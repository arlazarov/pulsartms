using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task PreparedQuantityKeepsLaterFullTankThroughSaveAndReopeningWithoutRouting()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    fixture.Stations.Insert(
      0,
      fixture.Stations[0] with
      {
        Id = Guid.NewGuid(),
        Name = "Earlier station",
        Longitude = -79.35m,
      }
    );
    fixture.Services.Reads.Invalidate("fuel");
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var edits = new List<FuelPlanEditStop>
    {
      new(fixture.Stations[0].Id, fixture.Load.Stops[^1].Id, 25, false),
      new(fixture.Stations[1].Id, fixture.Load.Stops[^1].Id, 0, true),
    };
    var rows = await FuelRowsAsync(fixture);
    var routingCalls = fixture.Router.Calls;
    var prepared = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, edits, QuantityStopIndex: 0),
      false,
      default
    );
    var option = Assert
      .IsType<FuelQuantityChoices>(prepared.QuantityChoices)
      .Options.Single(x => x.SliderGallons == 35);

    Assert.Empty(prepared.Errors);
    Assert.Empty(option.Errors);
    Assert.True(option.Visits[1].FillToTarget);
    Assert.Equal(
      prepared.Plan.Stops[1].BuyGallons - 10,
      option.Visits[1].BuyGallons,
      8
    );
    Assert.Equal(
      prepared.FillLimitGallons,
      option.Visits[1].DepartureGallons,
      8
    );
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    var chosen = prepared
      .Stops.Select(
        (edit, index) =>
          edit with
          {
            BuyGallons = option.Visits[index].BuyGallons,
            FillToTarget = option.Visits[index].FillToTarget,
          }
      )
      .ToList();
    var saved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(prepared.ExpectedCalculatedAt, chosen),
      true,
      default
    );
    var snapshot = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var compatibility = JsonSerializer.Deserialize<RoutePlan>(
      (
        await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
      ).PlanJson,
      RoutePlanningService.Json
    )!;

    Assert.Empty(saved.Errors);
    Assert.True(snapshot.Plan.Stops[1].FillToTarget);
    Assert.True(compatibility.FuelPlan!.Stops[1].FillToTarget);
    Assert.Equal(
      option.Visits[1].BuyGallons,
      snapshot.Plan.Stops[1].BuyGallons,
      8
    );
    Assert.Equal(option.PurchaseCostUsd, snapshot.Plan.PurchaseCostUsd, 8);
    var reopened = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null, 0),
      false,
      default
    );
    var next = Assert
      .IsType<FuelQuantityChoices>(reopened.QuantityChoices)
      .Options.Single(x => x.SliderGallons == 45);

    Assert.True(reopened.Stops[1].FillToTarget);
    Assert.Empty(next.Errors);
    Assert.True(next.Visits[1].FillToTarget);
    Assert.Equal(
      option.Visits[1].BuyGallons - 10,
      next.Visits[1].BuyGallons,
      8
    );
    Assert.Equal(reopened.FillLimitGallons, next.Visits[1].DepartureGallons, 8);
    Assert.Equal(routingCalls, fixture.Router.Calls);
  }

  [Fact]
  public async Task PreparedThirtyFiveGallonChoiceSavesRedistributionAndRejectsStaleRevisionWithoutRouting()
  {
    await using var fixture = await CreateFuelEditingFixtureAsync();
    fixture.Stations.Insert(
      0,
      fixture.Stations[0] with
      {
        Id = Guid.NewGuid(),
        Name = "Earlier station",
        Longitude = -79.35m,
      }
    );
    fixture.Services.Reads.Invalidate("fuel");
    var initial = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(null, null),
      false,
      default
    );
    var edits = new List<FuelPlanEditStop>
    {
      new(fixture.Stations[0].Id, fixture.Load.Stops[^1].Id, 25, false),
      new(fixture.Stations[1].Id, fixture.Load.Stops[^1].Id, 100, false),
    };
    var rows = await FuelRowsAsync(fixture);
    var routingCalls = fixture.Router.Calls;
    var clockCalls = fixture.Services.Hos.ClockCalls;
    var historyCalls = fixture.Services.Hos.HistoryCalls;

    var prepared = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(initial.ExpectedCalculatedAt, edits, QuantityStopIndex: 0),
      false,
      default
    );

    Assert.Empty(prepared.Errors);
    Assert.Equal(rows, await FuelRowsAsync(fixture));
    var choices = Assert.IsType<FuelQuantityChoices>(prepared.QuantityChoices);
    Assert.Equal(0, choices.StopIndex);
    var option = Assert.Single(
      choices.Options,
      choice => choice.SliderGallons == 35
    );
    Assert.False(option.FillToTarget);
    Assert.Empty(option.Errors);
    Assert.Equal([35d, 90d], option.Visits.Select(visit => visit.BuyGallons));
    Assert.Equal(prepared.Plan.PurchaseGallons, option.PurchaseGallons, 8);
    Assert.Equal(prepared.Plan.ArrivalGallons, option.ArrivalGallons, 8);
    Assert.Equal([25d, 100d], edits.Select(edit => edit.BuyGallons));
    Assert.Equal(clockCalls, fixture.Services.Hos.ClockCalls);
    Assert.Equal(historyCalls, fixture.Services.Hos.HistoryCalls);

    var chosen = prepared
      .Stops.Select(
        (edit, index) =>
          edit with
          {
            BuyGallons = option.Visits[index].BuyGallons,
            FillToTarget = option.Visits[index].FillToTarget,
            PurchaseLimitGallons = 999,
          }
      )
      .ToList();
    var saved = await fixture.Services.Fuel.EditAsync(
      fixture.Load.Id,
      new(prepared.ExpectedCalculatedAt, chosen),
      true,
      default
    );

    Assert.Empty(saved.Errors);
    Assert.NotEqual(prepared.ExpectedCalculatedAt, saved.ExpectedCalculatedAt);
    Assert.Equal([35d, 90d], saved.Plan.Stops.Select(stop => stop.BuyGallons));
    Assert.Equal(option.ArrivalGallons, saved.Plan.ArrivalGallons, 8);
    Assert.Equal(option.PurchaseGallons, saved.Plan.PurchaseGallons, 8);
    Assert.Equal(option.PurchaseCostUsd, saved.Plan.PurchaseCostUsd, 8);
    Assert.All(
      saved.Stops,
      stop => Assert.NotEqual(999d, stop.PurchaseLimitGallons)
    );
    var snapshot = Assert.IsType<TruckFuelPlanSnapshot>(
      await fixture.Services.FuelPlans.ReadCheckedAsync(
        fixture.Truck.Id,
        default
      )
    );
    var compatibility = JsonSerializer.Deserialize<RoutePlan>(
      (
        await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
      ).PlanJson,
      RoutePlanningService.Json
    )!;
    Assert.True(snapshot.Plan.ManuallyEdited);
    Assert.Equal(saved.ExpectedCalculatedAt, snapshot.CalculatedAt);
    Assert.Equal(snapshot.CalculatedAt, compatibility.FuelPlan!.CalculatedAt);
    Assert.Equal(
      [35d, 90d],
      snapshot.Plan.Stops.Select(stop => stop.BuyGallons)
    );
    Assert.Equal(
      [35d, 90d],
      compatibility.FuelPlan.Stops.Select(stop => stop.BuyGallons)
    );
    var committed = await FuelRowsAsync(fixture);

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        fixture.Services.Fuel.EditAsync(
          fixture.Load.Id,
          new(prepared.ExpectedCalculatedAt, chosen),
          true,
          default
        )
    );

    Assert.Equal(committed, await FuelRowsAsync(fixture));
    Assert.Equal(routingCalls, fixture.Router.Calls);
    Assert.Equal(clockCalls, fixture.Services.Hos.ClockCalls);
    Assert.Equal(historyCalls, fixture.Services.Hos.HistoryCalls);
  }
}
