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

  private MessagingMailboxes Mailboxes(
    int limit = MessagingMailboxes.Limit,
    int perAccount = MessagingMailboxes.PerAccount,
    int held = MessagingMailboxes.Held
  ) => new(events, clock, limit, perAccount, held);

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
    var answer = Answered(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
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

    var answer = await Ask(mailboxes, Company.Amf, Ann, box, default);
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

    var answer = Answered(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.Equal((box, false), (answer.Mailbox, answer.Resync));
    Assert.Empty(answer.Conversations);
  }

  [Fact]
  public async Task AMailboxIsItsOwnersAlone()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);
    var other = Guid.NewGuid();

    var stranger = await Ask(mailboxes, Company.Amf, "bob", box, default);
    var foreign = await Ask(mailboxes, other, Ann, box, default);

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
      (await Ask(mailboxes, Company.Amf, Ann, box, default)).Conversations
    );
  }

  [Fact]
  public async Task AnUnknownOrExpiredMailboxIsReplacedWithAResync()
  {
    var mailboxes = Mailboxes();
    var box = await OpenAsync(mailboxes);

    var unknown = await Ask(
      mailboxes,
      Company.Amf,
      Ann,
      Guid.NewGuid(),
      default
    );
    Assert.True(unknown.Resync);

    clock.Advance(MessagingMailboxes.Keep + TimeSpan.FromSeconds(1));
    var expired = await Ask(mailboxes, Company.Amf, Ann, box, default);
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

    var answer = Answered(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
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

    var answer = await Ask(mailboxes, Company.Amf, Ann, box, default);
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
      (Answered(await first.WaitAsync(TimeSpan.FromSeconds(5)))).Conversations
    );
    clock.Advance(MessagingMailboxes.Wait);
    var late = Answered(await second.WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.Equal((box, false), (late.Mailbox, late.Resync));
    Assert.Empty(late.Conversations);
  }

  [Fact]
  public async Task TheProcessKeepsAtMostTheLimitDroppingTheIdlest()
  {
    var mailboxes = Mailboxes(perAccount: int.MaxValue);
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
      (await Ask(mailboxes, Company.Amf, Ann, oldest, default)).Resync
    );
  }

  // Opens racing at the limit: the process never holds more than it, and
  // each open either takes an idle mailbox's place or is refused.
  [Fact]
  public async Task ConcurrentOpensAtTheLimitNeverExceedIt()
  {
    const int limit = 8;
    var mailboxes = Mailboxes(limit, perAccount: int.MaxValue);
    for (var i = 0; i < limit; i++)
      await OpenAsync(mailboxes);
    var start = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var most = 0;

    var racing = Enumerable
      .Range(0, 64)
      .Select(_ =>
        Task.Run(async () =>
        {
          await start.Task;
          var opened = await mailboxes.WaitAsync(
            Company.Amf,
            Ann,
            null,
            default
          );
          InterlockedMax(ref most, mailboxes.Count);
          return opened;
        })
      )
      .ToList();
    start.SetResult();
    var opens = await Task.WhenAll(racing);

    Assert.InRange(most, 1, limit);
    Assert.Equal(limit, mailboxes.Count);
    Assert.All(opens, x => Assert.True(x is null || x.Resync));
  }

  // Every mailbox held by a waiting request: a new one is refused rather
  // than admitted over the limit, and admitted once one is let go.
  [Fact]
  public async Task WhenEveryMailboxIsHeldANewOneIsRefused()
  {
    const int limit = 4;
    var mailboxes = Mailboxes(limit);
    var boxes = new List<Guid>();
    for (var i = 0; i < limit; i++)
      boxes.Add(await OpenAsync(mailboxes));
    var held = boxes
      .Select(box => mailboxes.WaitAsync(Company.Amf, Ann, box, default))
      .ToList();

    Assert.Null(await mailboxes.WaitAsync(Company.Amf, "bob", null, default));
    Assert.Equal(limit, mailboxes.Count);

    clock.Advance(MessagingMailboxes.Wait);
    await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(5));
    Assert.NotNull(
      await mailboxes.WaitAsync(Company.Amf, "bob", null, default)
    );
    Assert.Equal(limit, mailboxes.Count);
  }

  // Eviction or expiry racing a request that asks for the mailbox: either
  // the request holds it first, and it stays open and hears the next
  // change, or it was already gone, and the request gets a new one with a
  // resync. Never a request waiting on a mailbox that was closed.
  [Theory]
  [InlineData("evicted", "asked first")]
  [InlineData("evicted", "opened first")]
  [InlineData("expired", "asked first")]
  [InlineData("expired", "opened first")]
  public async Task AMailboxBeingAskedForIsNeverClosedUnderIt(
    string closing,
    string order
  )
  {
    var mailboxes = Mailboxes(limit: closing == "evicted" ? 1 : 8);
    var box = await OpenAsync(mailboxes);
    Task<MessagingChanges?> asked;
    MessagingChanges? opened;
    if (order == "asked first")
    {
      // Held before it expires; its idle time passes Keep while held.
      if (closing == "expired")
        clock.Advance(TimeSpan.FromSeconds(50));
      asked = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
      if (closing == "expired")
        clock.Advance(TimeSpan.FromSeconds(15));
      opened = await mailboxes.WaitAsync(Company.Amf, "bob", null, default);
    }
    else
    {
      if (closing == "expired")
        clock.Advance(MessagingMailboxes.Keep + TimeSpan.FromSeconds(1));
      opened = await mailboxes.WaitAsync(Company.Amf, "bob", null, default);
      asked = mailboxes.WaitAsync(Company.Amf, Ann, box, default);
    }
    var chat = Guid.NewGuid();
    events.Publish(Company.Amf, new(chat, 1));

    var answer = Answered(await asked.WaitAsync(TimeSpan.FromSeconds(5)));
    if (order == "asked first")
    {
      Assert.Equal((box, false), (answer.Mailbox, answer.Resync));
      Assert.Equal([chat], answer.Conversations);
      if (closing == "evicted")
        Assert.Null(opened);
    }
    else
    {
      Assert.True(answer.Resync);
      Assert.NotEqual(box, answer.Mailbox);
    }
  }

  // The same race with no order imposed, many times: whichever wins, the
  // invariant holds and the limit is kept.
  [Fact]
  public async Task EvictionRacingARequestKeepsTheInvariant()
  {
    for (var round = 0; round < 300; round++)
    {
      var mailboxes = Mailboxes(limit: 1);
      var box = await OpenAsync(mailboxes);
      var start = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
      );
      var asking = Task.Run(async () =>
      {
        await start.Task;
        return mailboxes.WaitAsync(Company.Amf, Ann, box, default);
      });
      var opening = Task.Run(async () =>
      {
        await start.Task;
        return await mailboxes.WaitAsync(Company.Amf, "bob", null, default);
      });
      start.SetResult();
      var opened = await opening;
      var chat = Guid.NewGuid();
      events.Publish(Company.Amf, new(chat, round));
      var answer = Answered(
        await (await asking).WaitAsync(TimeSpan.FromSeconds(5))
      );

      if (answer.Mailbox == box)
      {
        Assert.False(answer.Resync);
        Assert.Contains(chat, answer.Conversations);
      }
      else
      {
        Assert.True(answer.Resync);
        Assert.NotNull(opened);
      }
      Assert.Equal(1, mailboxes.Count);
    }
  }

  // One browser opening mailbox after mailbox pushes out only its own
  // oldest, never another account's, however much older that one is.
  [Fact]
  public async Task AnAccountKeepsItsShareEvictingOnlyItsOwnOldest()
  {
    var mailboxes = Mailboxes(perAccount: 2);
    var bob = await Ask(mailboxes, Company.Amf, "bob", null, default);
    clock.Advance(TimeSpan.FromSeconds(1));
    var first = await OpenAsync(mailboxes);
    clock.Advance(TimeSpan.FromSeconds(1));
    var second = await OpenAsync(mailboxes);
    clock.Advance(TimeSpan.FromSeconds(1));
    await OpenAsync(mailboxes);

    Assert.Equal(3, mailboxes.Count);
    Assert.True(
      (await Ask(mailboxes, Company.Amf, Ann, first, default)).Resync
    );
    var waiting = mailboxes.WaitAsync(Company.Amf, "bob", bob.Mailbox, default);
    var chat = Guid.NewGuid();
    events.Publish(Company.Amf, new(chat, 1));
    Assert.Equal(
      [chat],
      Answered(await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Conversations
    );
    Assert.NotEqual(first, second);
  }

  // Every one of an account's mailboxes held by a waiting request: its next
  // is refused, and another account is still admitted.
  [Fact]
  public async Task AnAccountWhoseMailboxesAreAllHeldIsRefusedAnother()
  {
    var mailboxes = Mailboxes(perAccount: 2);
    var boxes = new[]
    {
      await OpenAsync(mailboxes),
      await OpenAsync(mailboxes),
    };
    var held = boxes
      .Select(box => mailboxes.WaitAsync(Company.Amf, Ann, box, default))
      .ToList();

    Assert.Null(await mailboxes.WaitAsync(Company.Amf, Ann, null, default));
    Assert.NotNull(
      await mailboxes.WaitAsync(Company.Amf, "bob", null, default)
    );

    clock.Advance(MessagingMailboxes.Wait);
    await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(5));
  }

  // At most so many requests wait at once, whoever they are: the rest of
  // the API needs the instance's other request slots. The one beyond is
  // refused without taking a lease, and admitted once a wait ends.
  [Fact]
  public async Task AtMostSoManyRequestsWaitAtOnce()
  {
    var mailboxes = Mailboxes(held: 2);
    var ann = await OpenAsync(mailboxes);
    var bob = (await Ask(mailboxes, Company.Amf, "bob", null, default)).Mailbox;
    var cy = (await Ask(mailboxes, Company.Amf, "cy", null, default)).Mailbox;
    var waiting = new[]
    {
      mailboxes.WaitAsync(Company.Amf, Ann, ann, default),
      mailboxes.WaitAsync(Company.Amf, "bob", bob, default),
    };

    Assert.Null(await mailboxes.WaitAsync(Company.Amf, "cy", cy, default));

    clock.Advance(MessagingMailboxes.Wait);
    await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(5));
    var admitted = mailboxes.WaitAsync(Company.Amf, "cy", cy, default);
    var chat = Guid.NewGuid();
    events.Publish(Company.Amf, new(chat, 1));
    Assert.Equal(
      [chat],
      Answered(await admitted.WaitAsync(TimeSpan.FromSeconds(5))).Conversations
    );
  }

  private static void InterlockedMax(ref int most, int value)
  {
    for (
      var seen = Volatile.Read(ref most);
      value > seen
        && Interlocked.CompareExchange(ref most, value, seen) != seen;
      seen = Volatile.Read(ref most)
    ) { }
  }

  private static MessagingChanges Answered(MessagingChanges? answer) =>
    Assert.IsType<MessagingChanges>(answer);

  private static async Task<MessagingChanges> Ask(
    MessagingMailboxes mailboxes,
    Guid company,
    string account,
    Guid? box,
    CancellationToken ct
  ) => Answered(await mailboxes.WaitAsync(company, account, box, ct));

  private static async Task<Guid> OpenAsync(MessagingMailboxes mailboxes)
  {
    var opened = await Ask(mailboxes, Company.Amf, Ann, null, default);
    Assert.True(opened.Resync);
    Assert.Empty(opened.Conversations);
    return opened.Mailbox;
  }
}
