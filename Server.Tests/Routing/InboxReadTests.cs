using Application.Features.Routing.Audit;
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
    // Three messages, recorded at revisions 1 to 3.
    var conversation = await ReceiveAsync(f, "+15550000001", 3);

    await ReadAsync(f, colleague, conversation, 99);
    Assert.Equal(3, (await InboxAsync(f, me)).Single().Unread);
    Assert.Equal(0, (await InboxAsync(f, colleague)).Single().Unread);

    // A marker past the current revision stops at it; an older one is
    // ignored.
    await ReadAsync(f, me, conversation, 1);
    await ReadAsync(f, me, conversation, 0);
    Assert.Equal(2, (await InboxAsync(f, me)).Single().Unread);
    Assert.Equal(
      3,
      (
        await f
          .Db.ConversationReads.AsNoTracking()
          .SingleAsync(x => x.UserId == colleague)
      ).ReadRevision
    );
    Assert.DoesNotContain(
      (await Handlers(f, me).Handle(new GetInboxQuery(true), default))
        .Response!
        .Conversations,
      x => x.Unread == 0
    );
  }

  [Fact]
  public async Task TheNoticeCountsUnreadConversationsInFixedReads()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, colleague) = await UsersAsync(f);
    var first = await ReceiveAsync(f, "+15550000001", 2);
    f.Counter.Reset();
    var one = await NoticeAsync(f, me);
    var reads = f.Counter.Reads;
    for (var i = 2; i <= 20; i++)
      await ReceiveAsync(f, $"+155500000{i:00}", 1);

    f.Counter.Reset();
    var many = await NoticeAsync(f, me);
    Assert.Equal(reads, f.Counter.Reads);
    Assert.Equal((1, false), (one.Conversations, one.More));
    Assert.Equal(new UnreadMark(first, 2), Assert.Single(one.Latest));
    Assert.Equal((20, false), (many.Conversations, many.More));
    // Newest arrival first.
    Assert.Equal(first, many.Latest[^1].ConversationId);

    await ReadAsync(f, colleague, first, 2);
    Assert.Equal(20, (await NoticeAsync(f, me)).Conversations);
    Assert.Equal(19, (await NoticeAsync(f, colleague)).Conversations);
  }

  // A message the provider delivers late carries an older time than the
  // one already read. It is still unread and still raises the notice,
  // while a claim or a reply, which change the conversation too, do not.
  [Fact]
  public async Task ALateMessageWithAnOlderTimeIsUnreadAndRaisesTheNotice()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, "+15550000001", 2);
    await ReadAsync(f, me, conversation, 2);
    Assert.Equal(0, (await NoticeAsync(f, me)).Conversations);

    await RecordAsync(f, "+15550000001", Start.AddHours(-1), "late");

    var notice = await NoticeAsync(f, me);
    Assert.Equal(new UnreadMark(conversation, 3), Assert.Single(notice.Latest));
    Assert.Equal(1, (await InboxAsync(f, me)).Single().Unread);
    await f.Db.Conversations.ExecuteUpdateAsync(x =>
      x.SetProperty(c => c.Revision, c => c.Revision + 5)
    );
    Assert.Equal(notice.Latest, (await NoticeAsync(f, me)).Latest);
    await ReadAsync(f, me, conversation, 8);
    Assert.Empty((await NoticeAsync(f, me)).Latest);
  }

  // The auditor finds a conversation whose latest arrival is behind one of
  // its own driver messages, and nothing else.
  [Fact]
  public async Task TheAuditorFindsAnArrivalThatHidesAMessage()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await UsersAsync(f);
    var healthy = await ReceiveAsync(f, "+15550000001", 2);
    var broken = await ReceiveAsync(f, "+15550000002", 3);
    await f
      .Db.Conversations.Where(x => x.Id == broken)
      .ExecuteUpdateAsync(x => x.SetProperty(c => c.LastInboundRevision, 1));
    var company = await f
      .Db.Conversations.AsNoTracking()
      .Select(x => x.CompanyId)
      .FirstAsync();

    var page = await new UnreadArrivalRule(f.Db).ReadAsync(
      new(company, DateTime.UtcNow, null, 10, TimeSpan.FromMinutes(30)),
      default
    );

    var found = Assert.Single(page.Observed);
    Assert.Equal(broken.ToString(), found.EntityKey);
    Assert.Equal("latest:1;arrived:3", found.Versions);
    Assert.NotEqual(healthy.ToString(), found.EntityKey);
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

  private static Task ReadAsync(
    DispatchSyncFixture f,
    Guid user,
    Guid conversation,
    long revision
  ) =>
    Handlers(f, user)
      .Handle(new MarkConversationReadCommand(conversation, revision), default);

  private static async Task RecordAsync(
    DispatchSyncFixture f,
    string phone,
    DateTime at,
    string id
  )
  {
    f.Db.ChangeTracker.Clear();
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        new DriverMessageInboundEvent(phone, at)
        {
          ProviderMessageId = $"wamid.{phone}.{id}",
          Text = id,
        },
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
  }

  private static async Task<UnreadNotice> NoticeAsync(
    DispatchSyncFixture f,
    Guid user
  ) =>
    (
      await Handlers(f, user).Handle(new GetUnreadNoticeQuery(), default)
    ).Response!;

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

    public Task<RequestResponse<UnreadNotice>> Handle(
      GetUnreadNoticeQuery q,
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
