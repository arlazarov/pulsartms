using System.Text.Json;
using Application.Features.Messaging.Services;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Entities.Messaging;
using Domain.Models.Fuel;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

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
    await AttemptAsync(f, saved);
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

  // An attempt that was in flight when the calculation read what the driver
  // held, and was accepted before its commit: the acceptance may become a
  // hand-over the calculation never saw, so the commit is refused.
  [Fact]
  public async Task AnAttemptAcceptedDuringTheCalculationRefusesItsCommit()
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var saved = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    var attempt = await AttemptAsync(f, saved);
    Reprice(f, a, 4.5m);
    f.Sender.BeforeFuel = async _ =>
    {
      f.Sender.BeforeFuel = null;
      await f
        .Db.DriverMessages.Where(x => x.Id == attempt)
        .ExecuteUpdateAsync(x =>
          x.SetProperty(m => m.Status, DriverMessageStatuses.Accepted)
            .SetProperty(m => m.StatusAt, DateTime.UtcNow.AddSeconds(1))
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
  }

  // Root's review of D6: Messaging's two-minute timeout does not prove the
  // provider stopped. A WhatsApp hand-over of stop A is held at the
  // provider; past the timeout the dispatcher saves the fuel plan through
  // the real owner - keeping A, or choosing C instead - and it commits
  // without knowing A went. Then the provider takes the message. The late
  // record reaches the prepared summaries; the plan read afterwards shows
  // A sent, or A withdrawn for review; and the driver got one message.
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AnAcceptanceHeldOverAPublicationLosesNoStopAndSendsOnce(
    bool kept
  )
  {
    var a = Station("A", 40m, -79.3m, 3.0m);
    var c = Station("C", 40m, -79.25m, 3.6m);
    await using var f = await Fixture.CreateAsync([a, c], pickedUp: true);
    await FueledAsync(f, DateTime.UtcNow.AddMinutes(-5));
    var first = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    var stop = Assert.Single(first.Plan.Stops);
    Assert.Equal(a.Id, stop.StationId);
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Driver",
      IsActive = true,
      WhatsAppPhone = "+15558234327",
    };
    f.Db.Drivers.Add(driver);
    // The driver wrote an hour ago, so Messaging's window is open.
    f.Db.Conversations.Add(
      new Conversation
      {
        Id = Guid.NewGuid(),
        Channel = DriverMessageChannels.WhatsApp,
        BusinessNumberId = "123456",
        Participant = driver.WhatsAppPhone,
        LastInboundAt = DateTime.UtcNow.AddHours(-1),
        LastMessageAt = DateTime.UtcNow.AddHours(-1),
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    var shown = new FuelIssuePreviews.Current(
      first,
      new(
        f.Truck.Id,
        first.CalculatedAt,
        first.RootExecutionLegId,
        first.AssignmentRevision,
        FuelIssueStates.Ready,
        null,
        false,
        [],
        "Fuel at A"
      )
      {
        Recipient = new(
          driver.Id,
          driver.Name,
          driver.WhatsAppPhone,
          FuelIssueChannelStates.Ready,
          null
        ),
      },
      [(stop, "Fuel at A")]
    );
    var request = new FuelIssueSendRequest(
      new(first.CalculatedAt, [FuelVisitIdentity.Key(stop)])
      {
        AssignmentRevision = first.AssignmentRevision,
      },
      false
    );
    // The request's own context and clock: its attempt was made three
    // minutes before the plan is saved, so it no longer holds plans.
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-3));
    var transport = new FakeDriverMessaging();
    var reached = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var held = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    transport.During = async () =>
    {
      reached.TrySetResult();
      await held.Task;
    };
    FuelIssueSender Issuer(AppDbContext db) =>
      new(
        new DriverTextDelivery(
          db,
          transport,
          new TestCompany(),
          clock,
          NullLogger<DriverTextDelivery>.Instance
        ),
        new FuelIssueRecords(
          db,
          f.Services.Summaries,
          new TestCompany(),
          Options.Create(new FuelIssueOptions()),
          TimeProvider.System,
          new PlanningPublicationScope(db)
        )
      );
    AppDbContext Context() =>
      new(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(f.Connection)
          .Options
      );
    await using var sender = Context();
    var sending = Issuer(sender)
      .SendAsync(
        request,
        _ => Task.FromResult<FuelIssuePreviews.Current?>(shown),
        "dispatcher",
        default
      );
    // The call reaches the provider; a send that ends before it says why.
    await Task.WhenAny(reached.Task, sending)
      .WaitAsync(TimeSpan.FromSeconds(10));
    Assert.False(
      sending.IsCompleted,
      sending.IsCompleted ? (await sending).Error : null
    );

    // While the call is held: the plan is saved through its owner and
    // committed, knowing nothing of A's hand-over.
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    f.Db.ChangeTracker.Clear();
    await f.Services.Fuel.EditAsync(
      f.Load.Id,
      new(
        first.CalculatedAt,
        [
          kept
            ? new(a.Id, stop.BeforeStopId, stop.BuyGallons, stop.FillToTarget)
            : new(c.Id, stop.BeforeStopId, 0, true),
        ]
      ),
      true,
      default
    );
    var second = (
      await f.Services.FuelPlans.ReadCheckedAsync(f.Truck.Id, default)
    )!;
    Assert.True(second.CalculatedAt > first.CalculatedAt);
    Assert.Equal(
      kept ? a.Id : c.Id,
      Assert.Single(second.Plan.Stops).StationId
    );
    Assert.Null(second.Plan.Withdrawn);
    Assert.Empty(await f.Db.FuelVisitSends.ToListAsync());
    // A plain press from another request meanwhile sends nothing.
    await using (var other = Context())
      Assert.Equal(
        409,
        (
          await Issuer(other)
            .SendAsync(
              request,
              _ => Task.FromResult<FuelIssuePreviews.Current?>(shown),
              "dispatcher",
              default
            )
        ).Status
      );
    // A board summary prepared from the plan as it now stands.
    var summary = new PlanningSummaryCache.Key(Company.Amf, f.Truck.Id);
    f.Services.Summaries.Keep(summary, "prepared");
    var prepared = f.Services.Summaries.Capture(summary, "prepared")!;

    clock.UtcNow = DateTimeOffset.UtcNow;
    held.SetResult();
    Assert.Equal(200, (await sending).Status);

    Assert.False(f.Services.Summaries.IsCurrent(prepared));
    var sent = await f.Db.FuelVisitSends.AsNoTracking().SingleAsync();
    Assert.Equal(
      (a.Id, first.CalculatedAt),
      (sent.StationId, sent.PlanCalculatedAt)
    );
    Assert.True(sent.SentAt >= second.CalculatedAt);
    f.Db.ChangeTracker.Clear();
    var read = await f.Reader.ForDispatchAsync(f.Load.Id, default);
    var fuel = read.State!.Plan!.FuelPlan!;
    if (kept)
    {
      Assert.NotNull(Assert.Single(fuel.Stops).Sent);
      Assert.Empty(fuel.Withdrawn ?? []);
    }
    else
    {
      Assert.Null(Assert.Single(fuel.Stops).Sent);
      Assert.Equal(a.Id, Assert.Single(fuel.Withdrawn ?? []).StationId);
    }
    // The same press from the view the dispatcher had records nothing new
    // and sends nothing: the provider already took this message.
    await using (var again = Context())
      Assert.Equal(
        200,
        (
          await Issuer(again)
            .SendAsync(
              request,
              _ => Task.FromResult<FuelIssuePreviews.Current?>(shown),
              "dispatcher",
              default
            )
        ).Status
      );
    Assert.Single(transport.Sent);
    Assert.Equal(1, await f.Db.FuelVisitSends.CountAsync());
  }

  private static async Task<Guid> AttemptAsync(
    Fixture f,
    TruckFuelPlanSnapshot saved
  )
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Driver",
    };
    f.Db.Drivers.Add(driver);
    var attempt = new DriverMessage
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
      StatusAt = DateTime.UtcNow.AddMinutes(-1),
      CreatedAt = DateTime.UtcNow.AddMinutes(-1),
    };
    f.Db.DriverMessages.Add(attempt);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return attempt.Id;
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
