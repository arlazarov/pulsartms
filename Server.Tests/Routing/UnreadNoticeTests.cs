using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Messaging;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Routing.InboxScenario;

namespace Server.Tests.Routing;

// The unread notice and the read marker: a fixed number of reads, arrivals
// counted by when PulsR recorded them, a notice that rises only for a new
// arrival, and a marker that never moves back between two writers.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class UnreadNoticeTests
{
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
    // Two messages then nineteen more: arrivals 1 to 21.
    Assert.Equal(new UnreadNotice(1, false, 2), one);
    Assert.Equal(new UnreadNotice(20, false, 21), many);

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
    Assert.Equal(new UnreadNotice(0, false, 0), await NoticeAsync(f, me));

    await RecordAsync(f, "+15550000001", Start.AddHours(-1), "late");

    Assert.Equal(new UnreadNotice(1, false, 3), await NoticeAsync(f, me));
    Assert.Equal(1, (await InboxAsync(f, me)).Single().Unread);
    await f.Db.Conversations.ExecuteUpdateAsync(x =>
      x.SetProperty(c => c.Revision, c => c.Revision + 5)
    );
    Assert.Equal(new UnreadNotice(1, false, 3), await NoticeAsync(f, me));
    await ReadAsync(f, me, conversation, 8);
    Assert.Equal(0, (await NoticeAsync(f, me)).Conversations);
  }

  // With more unread conversations than the notice lists, reading one of
  // those listed brings an older one into view. Its arrival is older, so
  // the newest arrival does not rise and nothing new is announced; a new
  // message does raise it.
  [Fact]
  public async Task AnOlderConversationComingIntoViewDoesNotRaiseTheNotice()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversations = new List<Guid>();
    for (var i = 1; i <= InboxHandlers.NoticeLimit + 2; i++)
      conversations.Add(await ReceiveAsync(f, $"+1555{i:0000000}", 1));
    var before = await NoticeAsync(f, me);
    Assert.Equal(new UnreadNotice(99, true, 101), before);

    await ReadAsync(f, me, conversations[^1], 1);
    await ReadAsync(f, me, conversations[^2], 1);
    var after = await NoticeAsync(f, me);
    Assert.Equal(new UnreadNotice(99, false, 99), after);
    Assert.True(after.Newest <= before.Newest);

    await RecordAsync(f, "+15550000001", Start, "new");
    Assert.Equal(102, (await NoticeAsync(f, me)).Newest);
  }

  // Two deliveries recorded at once both read the company's arrival
  // sequence. The second to commit fails as a write conflict rather than
  // committing a number equal to or below the first, and the provider's
  // retry records it after, so arrivals rise in commit order.
  [Fact]
  public async Task ArrivalsRiseInCommitOrder()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    await ReceiveAsync(f, "+15550000001", 1);
    await using var first = f.NewContext();
    await using var second = f.NewContext();

    await Recorder(first, "+15550000002");
    await Recorder(second, "+15550000003");
    await first.SaveChangesAsync();
    var conflict = await Record.ExceptionAsync(() => second.SaveChangesAsync());

    Assert.True(second.IsWriteConflict(conflict!));
    Assert.Equal(new UnreadNotice(2, false, 2), await NoticeAsync(f, me));
    await RecordAsync(f, "+15550000003", Start, "retry");
    Assert.Equal(new UnreadNotice(3, false, 3), await NoticeAsync(f, me));

    static Task Recorder(AppDbContext db, string phone) =>
      new InboxRecorder(db, TimeProvider.System).RecordAsync(
        DriverMessageChannels.WhatsApp,
        "123456",
        [
          new DriverMessageInboundEvent(phone, Start)
          {
            ProviderMessageId = $"wamid.{phone}.race",
            Text = "race",
          },
        ],
        default
      );
  }

  // Two writers, in either order, and a late lower marker after a higher
  // one: the marker keeps the higher revision. The upsert has no read
  // before its write for another writer to slip between.
  [Fact]
  public async Task TheReadMarkerKeepsTheHigherOfTwoWriters()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, colleague) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, "+15550000001", 12);
    var company = await f
      .Db.Conversations.AsNoTracking()
      .Select(x => x.CompanyId)
      .FirstAsync();
    var markers = new ConversationReadMarkers(f.Db);

    await markers.AdvanceAsync(company, conversation, me, 10, default);
    await markers.AdvanceAsync(company, conversation, me, 7, default);
    await markers.AdvanceAsync(company, conversation, colleague, 7, default);
    await markers.AdvanceAsync(company, conversation, colleague, 10, default);

    var reads = await f
      .Db.ConversationReads.AsNoTracking()
      .OrderBy(x => x.UserId == me)
      .Select(x => new { x.UserId, x.ReadRevision })
      .ToListAsync();
    Assert.Equal(2, reads.Count);
    Assert.All(reads, x => Assert.Equal(10, x.ReadRevision));
    Assert.Equal(2, (await InboxAsync(f, me)).Single().Unread);
  }
}
