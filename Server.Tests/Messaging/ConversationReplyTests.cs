using System.Data.Common;
using Application.Features.Messaging.Background;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Messaging;

// Replies from a conversation: committed before anything is sent, sent once
// by the fenced outbox, never resent without a dispatcher asking, refused
// outside the driver's 24-hour window, and refused as stale when the driver
// or a colleague wrote since the dispatcher looked.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ConversationReplyTests
{
  [Fact]
  public async Task AReplyIsQueuedThenSentOnceAndARepeatedPressReturnsIt()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var key = Guid.NewGuid();
    using var listening = f.Events.Subscribe(Company.Amf);

    var queued = await f.SendAsync(
      new(conversation, "On my way", key, last, false)
    );
    var again = await f.SendAsync(
      new(conversation, "On my way", key, last, false)
    );
    Assert.Equal(
      (OutboundStates.Queued, queued.Response!.Id),
      (queued.Response.Status, again.Response!.Id)
    );

    Assert.Equal(1, await f.Worker.RunOnceAsync(default));
    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    var sent = await f.MessageAsync(queued.Response.Id);
    Assert.Equal(
      (DriverMessageStatuses.Accepted, "wamid.1"),
      (sent.Status, sent.ProviderMessageId)
    );
    Assert.Equal([("+15558234327", "On my way")], f.Messaging.Sent);
    Assert.True(listening.Reader.TryRead(out _));
    Assert.True(listening.Reader.TryRead(out _));
  }

  [Fact]
  public async Task OutsideTheWindowOrWithAReusedKeyNothingIsQueued()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync(hoursAgo: 25);
    Assert.Equal(
      409,
      (
        await f.SendAsync(
          new(conversation, "Hello", Guid.NewGuid(), last, false)
        )
      ).StatusCode
    );

    var (open, seen) = await f.ConversationAsync("+15550000002");
    var key = Guid.NewGuid();
    await f.SendAsync(new(open, "First", key, seen, false));
    Assert.Equal(
      409,
      (
        await f.SendAsync(new(open, "Something else", key, seen, false))
      ).StatusCode
    );
    Assert.Single(
      await f
        .Db.ConversationMessages.Where(x => x.Direction == "out")
        .ToListAsync()
    );
  }

  // The driver wrote again while the dispatcher was typing: the reply is
  // refused until the dispatcher has seen it or confirms.
  [Fact]
  public async Task AReplyToAConversationThatMovedOnIsRefusedUnlessConfirmed()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, seen) = await f.ConversationAsync();
    await f.InboundAsync("+15558234327", "wamid.later", "Never mind");
    f.Clock.Advance(TimeSpan.FromSeconds(2));

    var refused = await f.SendAsync(
      new(conversation, "Ok", Guid.NewGuid(), seen, false)
    );
    Assert.Equal(409, refused.StatusCode);
    Assert.Contains("newer message", refused.Errors![0]);
    var confirmed = await f.SendAsync(
      new(conversation, "Ok", Guid.NewGuid(), seen, true)
    );
    Assert.True(confirmed.Success);

    // Their own last reply does not make a dispatcher's next one stale.
    var next = await f.SendAsync(
      new(conversation, "And", Guid.NewGuid(), seen, true)
    );
    Assert.True(next.Success);
    Assert.True(
      (
        await f.SendAsync(
          new(conversation, "More", Guid.NewGuid(), next.Response!.Id, false)
        )
      ).Success
    );
  }

  // The worker pauses past its lease while the provider takes the message.
  // Another pass finds it "sending" and marks it unknown; the paused
  // worker's answer then records the provider's id on it. One message went,
  // and nothing sends it again.
  [Fact]
  public async Task AWorkerThatLostItsLeaseMidSendNeverCausesASecondSend()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false))
    ).Response!;
    f.Messaging.During = async () =>
    {
      f.Messaging.During = null;
      f.Clock.Advance(OutboundMessageOperation.Lease + TimeSpan.FromSeconds(1));
      await f.Worker.RunOnceAsync(default);
      Assert.Equal(
        DriverMessageStatuses.Unknown,
        (await f.MessageAsync(queued.Id)).Status
      );
    };

    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    var message = await f.MessageAsync(queued.Id);
    Assert.Equal(
      (DriverMessageStatuses.Accepted, "wamid.1"),
      (message.Status, message.ProviderMessageId)
    );
    await f.Worker.RunOnceAsync(default);
    Assert.Single(f.Messaging.Sent);
  }

  // Worker A read the reply and paused before taking it; worker B took it
  // and sent it. A's take then finds the fence moved and A sends nothing.
  [Fact]
  public async Task AWorkerThatPausedBeforeTakingAReplyNeverSendsItAsAnother()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false));
    var other = new BeforeFirstTake(() => f.Worker.RunOnceAsync(default));
    f.Interceptors.Add(other);

    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    Assert.True(other.Ran);
    Assert.Single(f.Messaging.Sent);
    Assert.Equal(
      (DriverMessageStatuses.Accepted, 1L),
      await f
        .Db.ConversationMessages.Where(x => x.Direction == "out")
        .Select(x => ValueTuple.Create(x.Status, x.Fence))
        .SingleAsync()
    );
  }

  // Worker A took the reply (fence 1) and paused; its lease ran out and
  // worker B took it (fence 2) but has not sent yet. A carries on with its
  // own fence, cannot say it is sending, and calls nobody; the reply is
  // B's to send.
  [Fact]
  public async Task AWorkerWhoseTakeWasOvertakenCannotSendWithTheNewFence()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false))
    ).Response!;
    f.Interceptors.Add(
      new AfterFirstTake(async () =>
      {
        f.Clock.Advance(
          OutboundMessageOperation.Lease + TimeSpan.FromSeconds(1)
        );
        var until =
          f.Clock.GetUtcNow().UtcDateTime + OutboundMessageOperation.Lease;
        await using var b = f.Context();
        await b
          .ConversationMessages.Where(x => x.Id == queued.Id)
          .ExecuteUpdateAsync(x =>
            x.SetProperty(m => m.Fence, m => m.Fence + 1)
              .SetProperty(m => m.LeaseUntil, until)
          );
      })
    );

    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    Assert.Empty(f.Messaging.Sent);
    var message = await f.MessageAsync(queued.Id);
    Assert.Equal((OutboundStates.Queued, 2L), (message.Status, message.Fence));
  }

  // The answer for attempt 1 arrives after it was marked unknown and the
  // dispatcher asked for attempt 2. It belongs to attempt 1 alone, and it
  // moves the conversation's revision and signals like any other change.
  [Fact]
  public async Task ALateAnswerBelongsToItsOwnAttemptAndIsSignalled()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var first = (
      await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false))
    ).Response!;
    Guid second = default;
    f.Messaging.During = async () =>
    {
      f.Messaging.During = null;
      f.Clock.Advance(OutboundMessageOperation.Lease + TimeSpan.FromSeconds(1));
      await f.Worker.RunOnceAsync(default);
      second = (
        await f.Replies()
          .Handle(new RetryConversationMessageCommand(first.Id), default)
      )
        .Response!
        .Id;
    };
    var before = await f.RevisionAsync(conversation);
    using var listening = f.Events.Subscribe(Company.Amf);

    await f.Worker.RunOnceAsync(default);

    var attempt1 = await f.MessageAsync(first.Id);
    var attempt2 = await f.MessageAsync(second);
    Assert.Equal(
      (DriverMessageStatuses.Accepted, "wamid.1"),
      (attempt1.Status, attempt1.ProviderMessageId)
    );
    Assert.Equal(
      (OutboundStates.Queued, (string?)null),
      (attempt2.Status, attempt2.ProviderMessageId)
    );
    Assert.True(await f.RevisionAsync(conversation) > before + 1);
    var signals = 0;
    while (listening.Reader.TryRead(out _))
      signals++;
    Assert.True(signals >= 3);
  }

  // The reply waited in the queue past the driver's window, or the
  // carrier's number changed meanwhile: it is withdrawn without calling the
  // provider.
  [Theory]
  [InlineData("window")]
  [InlineData("number")]
  [InlineData("unconfigured")]
  public async Task AReplyThatCanNoLongerGoIsWithdrawnWithoutACall(string cause)
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false))
    ).Response!;
    if (cause == "window")
      f.Clock.Advance(TimeSpan.FromHours(24));
    else if (cause == "number")
      f.Messaging.BusinessNumber = "999999";
    else
      f.Messaging.Configured = false;

    await f.Worker.RunOnceAsync(default);

    Assert.Equal(
      DriverMessageStatuses.Withdrawn,
      (await f.MessageAsync(queued.Id)).Status
    );
    Assert.Empty(f.Messaging.Sent);
  }

  [Fact]
  public async Task AnUnansweredReplyWaitsForTheDispatcherToSendItAgain()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await f.SendAsync(new(conversation, "Hi", Guid.NewGuid(), last, false))
    ).Response!;
    f.Messaging.Answers.Enqueue(new(DriverMessageOutcome.Unknown));

    await f.Worker.RunOnceAsync(default);
    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      DriverMessageStatuses.Unknown,
      (await f.MessageAsync(queued.Id)).Status
    );
    Assert.Single(f.Messaging.Sent);

    var retry = await f.Replies()
      .Handle(new RetryConversationMessageCommand(queued.Id), default);
    Assert.True(retry.Success);
    await f.Worker.RunOnceAsync(default);
    Assert.Equal(2, f.Messaging.Sent.Count);
    Assert.Equal(
      [1, 2],
      await f
        .Db.ConversationMessages.Where(x => x.Direction == "out")
        .Select(x => x.Attempt)
        .OrderBy(x => x)
        .ToListAsync()
    );
    Assert.Equal(
      409,
      (
        await f.Replies()
          .Handle(
            new RetryConversationMessageCommand(retry.Response!.Id),
            default
          )
      ).StatusCode
    );
  }

  [Fact]
  public async Task AClaimShowsWhoIsAnsweringForTwoMinutes()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync();

    Assert.True(
      (
        await f.Replies()
          .Handle(new ClaimConversationCommand(conversation), default)
      ).Response
    );
    Assert.False(
      (
        await f.Replies("colleague")
          .Handle(new ClaimConversationCommand(conversation), default)
      ).Response
    );
    f.Clock.Advance(TimeSpan.FromMinutes(3));
    Assert.True(
      (
        await f.Replies("colleague")
          .Handle(new ClaimConversationCommand(conversation), default)
      ).Response
    );
  }

  // Runs another worker's pass just before the first take of a reply.
  private sealed class BeforeFirstTake(Func<Task> other) : DbCommandInterceptor
  {
    private int ran;
    public bool Ran => ran == 1;

    public override async ValueTask<
      InterceptionResult<int>
    > NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains("SET \"Fence\"")
        && Interlocked.Exchange(ref ran, 1) == 0
      )
        await other();
      return result;
    }
  }

  // Runs something just after the first take of a reply committed.
  private sealed class AfterFirstTake(Func<Task> then) : DbCommandInterceptor
  {
    private int ran;

    public override async ValueTask<int> NonQueryExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      int result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains("SET \"Fence\"")
        && result == 1
        && Interlocked.Exchange(ref ran, 1) == 0
      )
        await then();
      return result;
    }
  }
}
