using Application.Features.Messaging.Audit;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Messaging.InboxScenario;

namespace Server.Tests.Messaging;

// The inbox as a dispatcher reads it: a fixed number of reads however many
// conversations there are, unread counts that are each dispatcher's own, a
// read marker that only moves forward, and change signals only for the
// dispatcher's own company.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class InboxReadTests
{
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

  // 120 conversations, 70 of them with the same last message time: the
  // list continues past each page of 50 in its own order, with no
  // conversation missed or repeated. One that gets a message while the
  // dispatcher pages moves to the top and is not shown again below.
  [Fact]
  public async Task TheInboxContinuesPastFiftyWithoutGapsOrRepeats()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    await ManyAsync(f, 120, same: 70);
    var expected = await f
      .Db.Conversations.AsNoTracking()
      .OrderByDescending(x => x.LastMessageAt)
      .ThenBy(x => x.Id)
      .Select(x => x.Id)
      .ToListAsync();

    var first = await PageAsync(f, me, null);
    var late = expected[110];
    await RecordAsync(
      f,
      await f
        .Db.Conversations.Where(x => x.Id == late)
        .Select(x => x.Participant)
        .SingleAsync(),
      Start.AddDays(1),
      "late"
    );
    var seen = first.Conversations.Select(x => x.Id).ToList();
    var next = first.Next;
    var pages = 1;
    while (next is not null)
    {
      var page = await PageAsync(f, me, next);
      seen.AddRange(page.Conversations.Select(x => x.Id));
      Assert.Equal(page.More, page.Next is not null);
      next = page.Next;
      pages++;
    }

    Assert.Equal(3, pages);
    Assert.Equal(seen.Count, seen.Distinct().Count());
    Assert.Equal([.. expected.Where(x => x != late)], seen);
    Assert.Equal(late, (await PageAsync(f, me, null)).Conversations[0].Id);
  }

  // A driver by any part of their name, a number by three or more of its
  // digits, however they are spaced; nothing else matches.
  [Fact]
  public async Task SearchFindsADriverByNameOrANumberByItsDigits()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    f.Db.Drivers.Add(
      new Domain.Entities.Fleet.Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = "d1",
        Name = "Alexei Morozov",
        IsActive = true,
        WhatsAppPhone = "+15558234327",
      }
    );
    await f.Db.SaveChangesAsync();
    var driver = await ReceiveAsync(f, "+15558234327", 1);
    var other = await ReceiveAsync(f, "+15550001111", 1);

    async Task<IEnumerable<Guid>> FindAsync(string term) =>
      (
        await Handlers(f, me).Handle(new GetInboxQuery(false, term), default)
      ).Response!.Conversations.Select(x => x.Id);

    Assert.Equal([driver], await FindAsync("moroz"));
    Assert.Equal([driver], await FindAsync("ALEX"));
    Assert.Equal([driver], await FindAsync("823-4327"));
    Assert.Equal([other], await FindAsync("(555) 000-1111"));
    Assert.Empty(await FindAsync("zz"));
    Assert.Empty(await FindAsync("55"));
    Assert.Equal(2, (await FindAsync("  ")).Count());
    Assert.Equal(
      400,
      (
        await Handlers(f, me)
          .Handle(new GetInboxQuery(false, new string('a', 101)), default)
      ).StatusCode
    );
  }

  private static async Task<InboxView> PageAsync(
    DispatchSyncFixture f,
    Guid user,
    InboxCursor? after
  ) =>
    (
      await Handlers(f, user)
        .Handle(new GetInboxQuery(false, null, after), default)
    ).Response!;

  // Count conversations in one notification; the first `same` of them
  // share one last message time, the rest are a minute apart.
  private static async Task ManyAsync(
    DispatchSyncFixture f,
    int count,
    int same
  )
  {
    f.Db.ChangeTracker.Clear();
    await new InboxRecorder(f.Db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. Enumerable
          .Range(0, count)
          .Select(i => new DriverMessageInboundEvent(
            $"+1555100{i:0000}",
            i < same ? Start : Start.AddMinutes(-i)
          )
          {
            ProviderMessageId = $"wamid.many.{i}",
            Text = $"message {i}",
          }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
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
}
