using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Messaging;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

// The inbox as a dispatcher reads it: a fixed number of reads however many
// conversations there are, unread counts that are each dispatcher's own, a
// read marker that only moves forward, and change signals only for the
// dispatcher's own company.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class InboxReadTests
{
  private static readonly DateTime Start = new(
    2026,
    9,
    21,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public async Task TheInboxCostsTheSameReadsForOneOrManyConversations()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    await ReceiveAsync(f, "+15550000001", 1);
    f.Counter.Reset();
    await Handlers(f, me).Handle(new GetInboxQuery(false), default);
    var one = f.Counter.Reads;

    for (var i = 2; i <= 30; i++)
      await ReceiveAsync(f, $"+155500000{i:00}", 3);
    f.Counter.Reset();
    var inbox = await Handlers(f, me).Handle(new GetInboxQuery(false), default);

    Assert.Equal(one, f.Counter.Reads);
    Assert.Equal(30, inbox.Response!.Conversations.Count);
  }

  [Fact]
  public async Task UnreadIsEachDispatchersOwnAndTheMarkerOnlyMovesForward()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, colleague) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, "+15550000001", 3);

    await Handlers(f, colleague)
      .Handle(
        new MarkConversationReadCommand(conversation, Start.AddDays(9)),
        default
      );
    Assert.Equal(3, (await InboxAsync(f, me)).Single().Unread);
    Assert.Equal(0, (await InboxAsync(f, colleague)).Single().Unread);

    // A marker past the newest message stops at it; an older one is ignored.
    await Handlers(f, me)
      .Handle(
        new MarkConversationReadCommand(conversation, Start.AddMinutes(1)),
        default
      );
    await Handlers(f, me)
      .Handle(new MarkConversationReadCommand(conversation, Start), default);
    Assert.Equal(1, (await InboxAsync(f, me)).Single().Unread);
    Assert.Equal(
      Start.AddMinutes(2),
      (
        await f
          .Db.ConversationReads.AsNoTracking()
          .SingleAsync(x => x.UserId == colleague)
      ).ReadThrough
    );
    Assert.DoesNotContain(
      (await Handlers(f, me).Handle(new GetInboxQuery(true), default))
        .Response!
        .Conversations,
      x => x.Unread == 0
    );
  }

  [Fact]
  public async Task AConversationReadsBackwardsInPages()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, "+15550000001", 60);

    var first = (
      await Handlers(f, me)
        .Handle(new GetConversationQuery(conversation, null), default)
    ).Response!;
    var second = (
      await Handlers(f, me)
        .Handle(
          new GetConversationQuery(conversation, first.Messages[^1].SentAt),
          default
        )
    ).Response!;

    Assert.Equal((50, true), (first.Messages.Count, first.Older));
    Assert.Equal((10, false), (second.Messages.Count, second.Older));
    Assert.Empty(
      first
        .Messages.Select(x => x.Id)
        .Intersect(second.Messages.Select(x => x.Id))
    );
  }

  [Fact]
  public async Task AStreamCarriesOnlyItsOwnCompanysSignals()
  {
    var events = new MessagingEvents();
    await using var f = await DispatchSyncFixture.CreateAsync();
    var handlers = new ConversationHandlers(
      f.Db,
      new Caller("a"),
      new TestCompany(),
      events,
      TimeProvider.System
    );
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var stream = handlers
      .Handle(new StreamMessagingEventsQuery(), stop.Token)
      .GetAsyncEnumerator(stop.Token);
    var next = stream.MoveNextAsync().AsTask();
    await Task.Delay(50);
    var mine = Guid.NewGuid();

    events.Publish(Guid.NewGuid(), new(Guid.NewGuid(), 1));
    events.Publish(Company.Amf, new(mine, 7));

    Assert.True(await next);
    Assert.Equal(
      (mine, 7L),
      (stream.Current.ConversationId, stream.Current.Revision)
    );
    await stop.CancelAsync();
  }

  private static async Task<IReadOnlyList<ConversationSummary>> InboxAsync(
    DispatchSyncFixture f,
    Guid user
  ) =>
    (await Handlers(f, user).Handle(new GetInboxQuery(false), default))
      .Response!
      .Conversations;

  private static async Task<(Guid, Guid)> UsersAsync(DispatchSyncFixture f)
  {
    var me = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "me",
      Name = "Me",
      Email = "me@example.invalid",
    };
    var colleague = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "colleague",
      Name = "Colleague",
      Email = "c@example.invalid",
    };
    f.Db.Users.AddRange(me, colleague);
    await f.Db.SaveChangesAsync();
    return (me.Id, colleague.Id);
  }

  // One minute apart from Start, from one number.
  private static async Task<Guid> ReceiveAsync(
    DispatchSyncFixture f,
    string phone,
    int count
  )
  {
    f.Db.ChangeTracker.Clear();
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. Enumerable
          .Range(0, count)
          .Select(i => new DriverMessageInboundEvent(phone, Start.AddMinutes(i))
          {
            ProviderMessageId = $"wamid.{phone}.{i}",
            Text = $"message {i}",
          }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return await f
      .Db.Conversations.AsNoTracking()
      .Where(x => x.Participant == phone)
      .Select(x => x.Id)
      .SingleAsync();
  }

  private static InboxAndConversation Handlers(DispatchSyncFixture f, Guid user)
  {
    f.Db.ChangeTracker.Clear();
    var identity = f
      .Db.Users.AsNoTracking()
      .Where(x => x.Id == user)
      .Select(x => x.IdentityUserId)
      .Single();
    return new(
      new InboxHandlers(f.Db, new Caller(identity), TimeProvider.System),
      new ConversationHandlers(
        f.Db,
        new Caller(identity),
        new TestCompany(),
        new MessagingEvents(),
        TimeProvider.System
      )
    );
  }

  private sealed class InboxAndConversation(
    InboxHandlers inbox,
    ConversationHandlers conversation
  )
  {
    public Task<RequestResponse<InboxView>> Handle(
      GetInboxQuery q,
      CancellationToken ct
    ) => inbox.Handle(q, ct);

    public Task<RequestResponse<bool>> Handle(
      MarkConversationReadCommand q,
      CancellationToken ct
    ) => inbox.Handle(q, ct);

    public Task<RequestResponse<ConversationView>> Handle(
      GetConversationQuery q,
      CancellationToken ct
    ) => conversation.Handle(q, ct);
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }
}
