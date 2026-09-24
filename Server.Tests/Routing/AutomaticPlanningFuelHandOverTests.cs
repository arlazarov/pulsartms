using System.Text.Json;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Entities.Messaging;
using Domain.Models.Fuel;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

// Fuel stops already given to the driver, through the real fuel owner: an
// automatic recalculation keeps them while they still work on the road as
// it is now (by the plan editor's own rules), replaces them only for a
// reason and then says so, never keeps another assignment's, and never
// commits over a hand-over it did not see. The delivery road runs east to
// 40, -79; the truck is at 40, -79.5, past its pickup.
public partial class AutomaticPlanningTests
{
  // A reroute two to four miles north: station A is still 9.5 miles from
  // the new road, within what the editor accepts. A cheaper station that
  // appeared meanwhile would win a fresh search; the driver's stop is kept.
  [Fact]
  public async Task ASmallDetourKeepsTheStationGivenToTheDriver()
  {
    var a = Station("A", 39.9m, -79.3m, 3.0m);
    await using var f = await Fixture.CreateAsync(
      [a],
      pickedUp: true,
      recalculationBudgetEnabled: false
    );
    var start = DateTime.UtcNow.AddMinutes(-9);
    await FueledAsync(f, start);
    var saved = await HandOverAsync(f);
    f.Stations.Add(Station("D", 40.03m, -79.3m, 2.5m));
    f.Services.Reads.Invalidate("fuel");

    await PassAsync(f, 40.06m, -79.45m, start.AddMinutes(1));
    var rerouted = await PassAsync(f, 40.06m, -79.45m, start.AddMinutes(2));
    Assert.True(rerouted.FromCurrentPosition);
    var refreshed = await RefreshAsync(f, saved);

    var stop = Assert.Single(refreshed.Stops);
    Assert.Equal(a.Id, stop.StationId);
    Assert.False(refreshed.ManuallyEdited);
    Assert.Contains(refreshed.Notes, x => x.Contains("were kept"));
    Assert.Null(refreshed.Withdrawn);
  }

  // A price change alone does not move a stop the driver holds, even one
  // large enough to send an untouched plan back to a search.
  [Fact]
  public async Task APriceChangeAloneDoesNotMoveAHandedOverStop()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = await HandOverAsync(f);
    Assert.Equal(a.Id, saved.Plan.Stops[0].StationId);
    Reprice(f, a, 4.5m);

    var refreshed = await RefreshAsync(f, saved);

    Assert.Equal(a.Id, Assert.Single(refreshed.Stops).StationId);
    Assert.Null(refreshed.Withdrawn);
  }

  // The station the driver was given is no longer offered: the search
  // replaces it, and the plan says the driver's stop was withdrawn until a
  // newer hand-over answers it. What was sent is not touched.
  [Fact]
  public async Task AStationNoLongerOfferedIsReplacedAndShownForReview()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = await HandOverAsync(f);
    f.Stations.Remove(a);
    f.Services.Reads.Invalidate("fuel");

    var refreshed = await RefreshAsync(f, saved);

    Assert.Equal(c.Id, Assert.Single(refreshed.Stops).StationId);
    var withdrawn = Assert.Single(refreshed.Withdrawn!);
    Assert.Equal(a.Id, withdrawn.StationId);
    Assert.Equal(1, await f.Db.FuelVisitSends.CountAsync());

    // Handing over the new plan answers it.
    var now = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    await Task.Delay(5);
    await f.Services.Issues.RecordAsync(
      now,
      [(now.Plan.Stops[0], "C")],
      FuelSendChannels.Manual,
      "dispatcher",
      default
    );
    var shown = Copy(now.Plan);
    await f.Services.Issues.ApplyAsync(now, shown, null, default);
    Assert.Empty(shown.Withdrawn!);
  }

  // A hand-over under another assignment revision is not this assignment's:
  // nothing is kept for it and nothing is shown as withdrawn.
  [Fact]
  public async Task AnotherAssignmentsHandOverKeepsNothing()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = await HandOverAsync(f);
    await f.Db.FuelVisitSends.ExecuteUpdateAsync(x =>
      x.SetProperty(s => s.AssignmentRevision, s => s.AssignmentRevision + 1)
    );
    Reprice(f, a, 4.5m);

    var refreshed = await RefreshAsync(f, saved);

    Assert.Equal(c.Id, Assert.Single(refreshed.Stops).StationId);
    Assert.Null(refreshed.Withdrawn);
  }

  // A hand-over recorded while the calculation runs, after it read what
  // the driver held: its commit is refused and nothing is written. The next
  // attempt sees the hand-over and keeps the stop.
  [Fact]
  public async Task AHandOverDuringTheCalculationRefusesItsCommit()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    Reprice(f, a, 4.5m);
    f.Sender.BeforeFuel = async _ =>
    {
      f.Sender.BeforeFuel = null;
      await f.Services.Issues.RecordAsync(
        saved,
        [(saved.Plan.Stops[0], "A")],
        FuelSendChannels.Manual,
        "dispatcher",
        default
      );
    };

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        f.Service.RecalculateFuelAsync(
          f.Load.Id,
          default,
          automaticRefreshRevision: saved.CalculatedAt
        )
    );

    Assert.Equal(
      saved.CalculatedAt,
      (
        await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
      )!.CalculatedAt
    );
    var retried = await RefreshAsync(f, saved);
    Assert.Equal(a.Id, Assert.Single(retried.Stops).StationId);
  }

  // A WhatsApp attempt in flight for the truck: the plan may be what it is
  // carrying, so a recalculation waits for its outcome.
  [Fact]
  public async Task AWhatsAppAttemptInFlightRefusesARecalculation()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Driver",
    };
    f.Db.Drivers.Add(driver);
    f.Db.DriverMessages.Add(
      new DriverMessage
      {
        Id = Guid.NewGuid(),
        DriverId = driver.Id,
        TruckId = f.Truck.Id,
        DispatchId = f.Load.Id,
        PlanCalculatedAt = saved.CalculatedAt,
        Channel = "whatsapp",
        Recipient = "+15550000000",
        Text = "Fuel at A",
        VisitKeys = "a",
        IdempotencyKey = "attempt",
        Status = DriverMessageStatuses.Sending,
        StatusAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    Reprice(f, a, 4.5m);

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(
      () =>
        f.Service.RecalculateFuelAsync(
          f.Load.Id,
          default,
          automaticRefreshRevision: saved.CalculatedAt
        )
    );
  }

  // A confirmation by hand against a plan that has been recalculated since
  // it was opened is refused under the truck's lock.
  [Fact]
  public async Task AConfirmationAgainstAnOlderPlanIsRefused()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    await using var f = await Fixture.CreateAsync([a], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var opened = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    f.Location.FuelPercent = 45;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    f.Db.ChangeTracker.Clear();
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);

    Assert.Null(
      await f.Services.Issues.RecordAsync(
        opened,
        [(opened.Plan.Stops[0], "A")],
        FuelSendChannels.Manual,
        "dispatcher",
        default,
        requireCurrent: true
      )
    );
    Assert.Equal(0, await f.Db.FuelVisitSends.CountAsync());
  }

  private static FuelStationDto Station(
    string name,
    decimal latitude,
    decimal longitude,
    decimal price
  )
  {
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    return new(
      Guid.NewGuid(),
      name,
      name,
      "Street",
      "City",
      "PA",
      "",
      "US",
      latitude,
      longitude,
      [
        new(
          "USD",
          "Diesel",
          price + .5m,
          price,
          .5m,
          today,
          today,
          price - .5m,
          "US gal"
        ),
      ]
    );
  }

  private static void Reprice(Fixture f, FuelStationDto station, decimal price)
  {
    var index = f.Stations.FindIndex(x => x.Id == station.Id);
    var discount = f.Stations[index].Discounts[0];
    f.Stations[index].Discounts[0] = discount with
    {
      RetailPrice = price + .5m,
      DiscountPrice = price,
      PriceAfterIfta = price - .5m,
    };
    f.Services.Reads.Invalidate("fuel");
  }

  // The route and a first automatic fuel plan, with a fresh fuel reading.
  private static async Task FueledAsync(Fixture f, DateTime at)
  {
    f.Location.FuelPercent = 40;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    await PassAsync(f, 40, -79.5m, at);
    f.Db.ChangeTracker.Clear();
    await f.Service.RecalculateFuelAsync(f.Load.Id, default);
  }

  // The first stop, handed over by hand as the plan says it now.
  private static async Task<TruckFuelPlanSnapshot> HandOverAsync(Fixture f)
  {
    var saved = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    Assert.Equal(
      1,
      await f.Services.Issues.RecordAsync(
        saved,
        [(saved.Plan.Stops[0], "Fuel stop")],
        FuelSendChannels.Manual,
        "dispatcher",
        default
      )
    );
    return saved;
  }

  // An automatic refresh of the saved plan, as the background refresh asks
  // for it; the plan as saved afterwards.
  private static async Task<FuelPlan> RefreshAsync(
    Fixture f,
    TruckFuelPlanSnapshot saved
  )
  {
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    f.Db.ChangeTracker.Clear();
    var current = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    await f.Service.RecalculateFuelAsync(
      f.Load.Id,
      default,
      automaticRefreshRevision: current.CalculatedAt
    );
    return (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!.Plan;
  }

  private static FuelPlan Copy(FuelPlan plan) =>
    JsonSerializer.Deserialize<FuelPlan>(
      JsonSerializer.Serialize(plan, RoutingJson.Options),
      RoutingJson.Options
    )!;
}
