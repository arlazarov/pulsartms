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
