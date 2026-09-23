using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Models.Messaging;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

// Sending over WhatsApp against a plan that moves: before the request, after
// it is accepted, and while the provider call is in flight. Each step reads
// the plan through a delegate the test controls, and the plan's hand-over
// state comes from the real records, as the preview reads it.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelIssueSenderTests
{
  private static readonly DateTime First = new(
    2026,
    9,
    23,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task APlanThatMovedBeforeTheRequestSendsNothing()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    var moved = await f.CurrentAsync(First.AddMinutes(1), gallons: 40);

    var outcome = await f.SendAsync(Request(shown), moved);

    Assert.Equal(409, outcome.Status);
    Assert.Empty(f.Transport.Sent);
    Assert.Empty(await f.Db.DriverMessages.ToListAsync());
  }

  [Fact]
  public async Task APlanThatMovesAfterAcceptanceIsWithdrawnBeforeTheCall()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    var moved = await f.CurrentAsync(First.AddMinutes(1), gallons: 40);

    var outcome = await f.SendAsync(Request(shown), shown, moved);

    Assert.Equal(409, outcome.Status);
    Assert.Empty(f.Transport.Sent);
    var attempt = await f.Db.DriverMessages.SingleAsync();
    Assert.Equal(DriverMessageStatuses.Withdrawn, attempt.Status);
    Assert.Empty(await f.Db.FuelVisitSends.ToListAsync());
  }

  [Fact]
  public async Task APlanThatMovesDuringTheCallKeepsWhatWasSentAndNeedsAnUpdate()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    var current = shown;
    f.Transport.During = async () =>
      current = await f.CurrentAsync(First.AddMinutes(1), gallons: 40);

    var outcome = await f.SendAsync(
      Request(shown),
      () => Task.FromResult<FuelIssuePreviews.Current?>(current)
    );

    Assert.Equal(200, outcome.Status);
    var send = await f.Db.FuelVisitSends.SingleAsync();
    Assert.Equal("full", send.Content);
    Assert.Equal(FuelSendChannels.WhatsApp, send.Channel);
    var message = await f.Db.DriverMessages.SingleAsync();
    Assert.Equal(message.Id, send.MessageId);
    Assert.Equal(DriverMessageStatuses.Accepted, message.Status);
    Assert.Equal(First, message.PlanCalculatedAt);

    // The plan now says forty gallons: the fill that went out is kept, and
    // the stop reads as changed since it was sent.
    var now = await f.CurrentAsync(First.AddMinutes(1), gallons: 40);
    var stop = now.Visits[0].Stop.Sent!;
    Assert.True(stop.Changed);
    Assert.Equal(DriverMessageStatuses.Accepted, stop.Delivery);
  }

  [Fact]
  public async Task TheSameInstructionIsOneMessageWhateverItsWording()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true, text: "about 40 mi");
    Assert.Equal(200, (await f.SendAsync(Request(shown), shown)).Status);

    // Pressed again on the same plan: the first attempt is the answer.
    Assert.Equal(200, (await f.SendAsync(Request(shown), shown)).Status);

    // The truck moved on and the fuel was recalculated to the same
    // instruction: already sent, so nothing new goes.
    var later = await f.CurrentAsync(
      First.AddMinutes(5),
      fill: true,
      text: "about 38 mi"
    );
    var again = await f.SendAsync(Request(later), later);

    Assert.Equal(409, again.Status);
    Assert.Single(f.Transport.Sent);
    Assert.Single(await f.Db.DriverMessages.ToListAsync());
  }

  [Fact]
  public async Task AnUnansweredSendIsRepeatedOnlyWhenADispatcherSaysSo()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    f.Transport.Answers.Enqueue(new(DriverMessageOutcome.Unknown));

    Assert.Equal(200, (await f.SendAsync(Request(shown), shown)).Status);
    Assert.Equal(
      DriverMessageStatuses.Unknown,
      (await f.Db.DriverMessages.SingleAsync()).Status
    );
    Assert.Empty(await f.Db.FuelVisitSends.ToListAsync());

    var blind = await f.SendAsync(Request(shown), shown);
    Assert.Equal(409, blind.Status);
    Assert.Single(f.Transport.Sent);

    var confirmed = await f.SendAsync(Request(shown, sendAgain: true), shown);
    Assert.Equal(200, confirmed.Status);
    Assert.Equal(2, f.Transport.Sent.Count);
    Assert.Equal(
      [1, 2],
      await f
        .Db.DriverMessages.OrderBy(x => x.Attempt)
        .Select(x => x.Attempt)
        .ToListAsync()
    );
  }

  [Fact]
  public async Task AnAttemptLeftSendingIsNotRepeatedUntilItTimesOut()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    // The process stopped mid-call: the attempt row stays "sending".
    f.Transport.During = () => throw new OperationCanceledException();
    await Assert.ThrowsAsync<OperationCanceledException>(
      () => f.SendAsync(Request(shown), shown)
    );
    f.Transport.During = null;

    Assert.Equal(409, (await f.SendAsync(Request(shown), shown)).Status);
    f.Time.Advance(TimeSpan.FromMinutes(3));
    Assert.Equal(409, (await f.SendAsync(Request(shown), shown)).Status);
    Assert.Equal(
      200,
      (await f.SendAsync(Request(shown, sendAgain: true), shown)).Status
    );
    Assert.Equal(2, f.Transport.Sent.Count);
  }

  [Fact]
  public async Task ARefusalCanBeSentAgainAndOnlyTheNumberIsKept()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    f.Transport.Answers.Enqueue(
      new(DriverMessageOutcome.Rejected, ErrorCode: 131026)
    );

    await f.SendAsync(Request(shown), shown);
    var refused = await f.Db.DriverMessages.SingleAsync();
    Assert.Equal(DriverMessageStatuses.Rejected, refused.Status);
    Assert.Equal(131026, refused.ErrorCode);
    Assert.Empty(await f.Db.FuelVisitSends.ToListAsync());

    Assert.Equal(200, (await f.SendAsync(Request(shown), shown)).Status);
    Assert.Equal(2, f.Transport.Sent.Count);
  }

  [Fact]
  public async Task ARecipientWhoseWindowIsClosedGetsNothing()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(
      First,
      fill: true,
      state: FuelIssueChannelStates.OutsideWindow
    );

    var outcome = await f.SendAsync(Request(shown), shown);

    Assert.Equal(409, outcome.Status);
    Assert.Contains("24 hours", outcome.Error);
    Assert.Empty(f.Transport.Sent);
  }

  [Fact]
  public async Task AnotherCarriersAttemptIsNotThisOnes()
  {
    await using var f = await Fixture.CreateAsync();
    var shown = await f.CurrentAsync(First, fill: true);
    f.Transport.Answers.Enqueue(new(DriverMessageOutcome.Unknown));
    await f.SendAsync(Request(shown), shown);

    // The same key under another carrier finds no attempt of its own.
    await using var scope = f.Refresh.NewScope();
    var company = scope.ServiceProvider.GetRequiredService<ICurrentCompany>();
    using var serving = company.As(Guid.NewGuid());
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    Assert.Empty(await db.DriverMessages.ToListAsync());
  }

  private static FuelIssueSendRequest Request(
    FuelIssuePreviews.Current shown,
    bool sendAgain = false
  ) =>
    new(
      new(
        shown.Saved.CalculatedAt,
        shown.Visits.Select(x => FuelVisitIdentity.Key(x.Stop)).ToList()
      )
      {
        AssignmentRevision = shown.Saved.AssignmentRevision,
      },
      sendAgain
    );

  private sealed class Fixture : IAsyncDisposable
  {
    public required PlanningRefreshFixture Refresh { get; init; }
    public FakeDriverMessaging Transport { get; } = new();
    public Guid Truck { get; private set; }
    public Guid Dispatch { get; private set; }
    public Guid Driver { get; private set; }
    public Guid Before { get; } = Guid.NewGuid();
    public Infrastructure.Persistence.AppDbContext Db => Refresh.Db;
    public ManualTimeProvider Time => Refresh.Time;

    public static async Task<Fixture> CreateAsync()
    {
      var f = new Fixture
      {
        Refresh = await PlanningRefreshFixture.CreateAsync(),
      };
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "t",
        UnitNumber = "54777",
        IsActive = true,
      };
      var driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = "d",
        Name = "Driver One",
        IsActive = true,
        WhatsAppPhone = "+15558234327",
      };
      var load = new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = 1441,
        Status = "in_transit",
        TruckId = truck.Id,
      };
      f.Db.AddRange(truck, driver, load);
      await f.Db.SaveChangesAsync();
      (f.Truck, f.Dispatch, f.Driver) = (truck.Id, load.Id, driver.Id);
      return f;
    }

    public FuelIssueRecords Records =>
      new(
        Db,
        Refresh.Services.GetRequiredService<PlanningSummaryCache>(),
        Refresh.Services.GetRequiredService<ICurrentCompany>(),
        Options.Create(new FuelIssueOptions()),
        Time
      );

    // What the preview reads: the saved plan, its stop marked with the
    // real hand-over records, and the recipient.
    public async Task<FuelIssuePreviews.Current> CurrentAsync(
      DateTime calculatedAt,
      bool fill = false,
      double gallons = 0,
      string text = "fuel at LOVES",
      string state = FuelIssueChannelStates.Ready
    )
    {
      var stop = new FuelPlanStop
      {
        StationId = new Guid("11111111-1111-1111-1111-111111111111"),
        DispatchId = Dispatch,
        BeforeStopId = Before,
        FillToTarget = fill,
        BuyGallons = gallons,
        ArrivalGallons = 60,
      };
      var plan = new FuelPlan { Stops = [stop] };
      var saved = new TruckFuelPlanSnapshot(
        Truck,
        Dispatch,
        calculatedAt,
        plan,
        [
          new(
            Dispatch,
            new PlanStop(Before, "Delivery", "", 1, new(40, -80)),
            100
          )
          {
            AssignmentRevision = 3,
          },
        ],
        null
      )
      {
        AssignmentRevision = 3,
      };
      await Records.ApplyAsync(saved, plan, null, default);
      var preview = new FuelIssuePreview(
        Truck,
        calculatedAt,
        null,
        3,
        FuelIssueStates.Ready,
        null,
        false,
        [],
        text
      )
      {
        Recipient = new(Driver, "Driver One", "+15558234327", state, null),
      };
      return new(saved, preview, [(stop, text)]);
    }

    public Task<FuelIssueSender.Outcome> SendAsync(
      FuelIssueSendRequest request,
      params FuelIssuePreviews.Current[] reads
    )
    {
      var next = 0;
      return SendAsync(
        request,
        () =>
          Task.FromResult<FuelIssuePreviews.Current?>(
            reads[Math.Min(next++, reads.Length - 1)]
          )
      );
    }

    public Task<FuelIssueSender.Outcome> SendAsync(
      FuelIssueSendRequest request,
      Func<Task<FuelIssuePreviews.Current?>> read
    )
    {
      Db.ChangeTracker.Clear();
      return new FuelIssueSender(
        Db,
        Transport,
        Records,
        Refresh.Services.GetRequiredService<ICurrentCompany>(),
        Time,
        NullLogger<FuelIssueSender>.Instance
      ).SendAsync(request, _ => read(), "dispatcher", default);
    }

    public ValueTask DisposeAsync() => Refresh.DisposeAsync();
  }
}
