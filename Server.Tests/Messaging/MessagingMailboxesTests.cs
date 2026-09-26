using Application.Features.Messaging.Services;
using Domain.Entities;
using Microsoft.Extensions.Time.Testing;

namespace Server.Tests.Messaging;

// A browser asks "what changed?" with requests that end, because Firebase
// Hosting held the server-sent stream back until it ended and inbound
// messages showed only on the 30-second poll. These pin that a change is
// answered as soon as it commits, that nothing raised between two requests
// is lost, and that a mailbox is its owner's alone.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class MessagingMailboxesTests
{
  private const string Ann = "ann";
  private readonly MessagingEvents events = new();
  private readonly FakeTimeProvider clock = new();

  private MessagingMailboxes Mailboxes() => new(events, clock);

  [Fact]
  public async Task AWaitingRequestAnswersAsSoonAsAChangeCommits()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var chat = Guid.NewGuid();

    var waiting = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    Assert.False(waiting.IsCompleted);
    events.Publish(Company.Amf, new(chat, 3));

    // No time passes on the clock: the answer does not wait for Wait.
    var answer = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal((box, false), (answer.Mailbox, answer.Resync));
    Assert.Equal([chat], answer.Conversations);
  }

  [Fact]
  public async Task AChangeBetweenTwoRequestsWaitsForTheNext()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();

    // Committed while no request of this browser is open.
    events.Publish(Company.Amf, new(first, 1));
    events.Publish(Company.Amf, new(second, 1));
    events.Publish(Company.Amf, new(first, 2));

    var answer = await mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    Assert.False(answer.Resync);
    Assert.Equal([first, second], answer.Conversations);
  }

  [Fact]
  public async Task NothingChangedAnswersEmptyAfterTheWait()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);

    var waiting = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    clock.Advance(MessagingMailboxes.Wait);

    var answer = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal((box, false), (answer.Mailbox, answer.Resync));
    Assert.Empty(answer.Conversations);
  }

  [Fact]
  public async Task AMailboxIsItsOwnersAlone()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var other = Guid.NewGuid();

    var stranger = await mailboxes.WaitAsync(Company.Amf, "bob", box, default);
    var foreign = await mailboxes.WaitAsync(other, Ann, box, default);

    // Neither reads Ann's queue: each gets a mailbox of its own and is
    // told to read everything.
    Assert.True(stranger.Resync && foreign.Resync);
    Assert.NotEqual(box, stranger.Mailbox);
    Assert.NotEqual(box, foreign.Mailbox);
    var chat = Guid.NewGuid();
    events.Publish(other, new(Guid.NewGuid(), 1));
    events.Publish(Company.Amf, new(chat, 1));
    Assert.Equal(
      [chat],
      (await mailboxes.WaitAsync(Company.Amf, Ann, box, default)).Conversations
    );
  }

  [Fact]
  public async Task AnUnknownOrExpiredMailboxIsReplacedWithAResync()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);

    var unknown = await mailboxes.WaitAsync(
      Company.Amf,
      Ann,
      Guid.NewGuid(),
      default
    );
    Assert.True(unknown.Resync);

    clock.Advance(MessagingMailboxes.Keep + TimeSpan.FromSeconds(1));
    var expired = await mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    Assert.True(expired.Resync);
    Assert.NotEqual(box, expired.Mailbox);
  }

  [Fact]
  public async Task AMailboxBeingReadNeverExpires()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    // Idle for most of Keep, then read: while it waits, the time since it
    // was last idle passes Keep, and another browser's request sweeps.
    clock.Advance(TimeSpan.FromSeconds(50));
    var waiting = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    clock.Advance(TimeSpan.FromSeconds(15));
    await OpenAsync(mailboxes);
    var chat = Guid.NewGuid();
    events.Publish(Company.Amf, new(chat, 1));

    var answer = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal((box, false), (answer.Mailbox, answer.Resync));
    Assert.Equal([chat], answer.Conversations);
  }

  [Fact]
  public async Task AFullQueueAnswersWithAResyncAndWhatQueued()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var burst = Enumerable
      .Range(0, MessagingEvents.QueueSize + 6)
      .Select(i => new MessagingEvent(Guid.NewGuid(), i))
      .ToList();
    foreach (var change in burst)
      events.Publish(Company.Amf, change);

    var answer = await mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    Assert.True(answer.Resync);
    Assert.Equal(MessagingEvents.QueueSize, answer.Conversations.Count);
    Assert.Equal(burst[0].ConversationId, answer.Conversations[0]);
  }

  [Fact]
  public async Task ASecondRequestForTheSameMailboxDoesNotTakeItsChange()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var first = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    var second = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    var chat = Guid.NewGuid();

    events.Publish(Company.Amf, new(chat, 1));

    Assert.Equal(
      [chat],
      (await first.WaitAsync(TimeSpan.FromSeconds(5))).Conversations
    );
    clock.Advance(MessagingMailboxes.Wait);
    var late = await second.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal((box, false), (late.Mailbox, late.Resync));
    Assert.Empty(late.Conversations);
  }

  [Fact]
  public async Task TheProcessKeepsAtMostTheLimitDroppingTheIdlest()
  {
    var mailboxes = Mailboxes();
    var oldest = await OpenAsync(mailboxes);
    for (var i = 1; i < MessagingMailboxes.Limit; i++)
    {
      clock.Advance(TimeSpan.FromMilliseconds(1));
      await OpenAsync(mailboxes);
    }
    Assert.Equal(MessagingMailboxes.Limit, mailboxes.Count);

    await OpenAsync(mailboxes);

    Assert.Equal(MessagingMailboxes.Limit, mailboxes.Count);
    Assert.True(
      (await mailboxes.WaitAsync(Company.Amf, Ann, oldest, default)).Resync
    );
  }

  private static async Task<Guid> OpenAsync(MessagingMailboxes mailboxes)
  {
    var opened = await mailboxes.WaitAsync(Company.Amf, Ann, null, default);
    Assert.True(opened.Resync);
    Assert.Empty(opened.Conversations);
    return opened.Mailbox;
  }
}
