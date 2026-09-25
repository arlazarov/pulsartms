using Application.Features.Messaging.Queries;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Messaging.InboxScenario;

namespace Server.Tests.Messaging;

// A conversation read backwards in pages: messages sharing one time are
// neither skipped nor repeated, and a driver message recorded late with an
// older time is never marked read before the dispatcher has been shown it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ConversationHistoryTests
{
  private const string Phone = "+15550000001";

  // 120 messages recorded together at one time: the old cursor (the time
  // alone) lost all but the first 50; the composite one reaches every
  // message once, in the database's own order.
  [Fact]
  public async Task AThreadContinuesPastFiftyMessagesAtOneTime()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    f.Db.ChangeTracker.Clear();
    await new Application.Features.Messaging.Services.InboxRecorder(
      f.Db,
      TimeProvider.System
    ).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. Enumerable
          .Range(0, 120)
          .Select(i => new DriverMessageInboundEvent(Phone, Start)
          {
            ProviderMessageId = $"wamid.same.{i}",
            Text = $"message {i}",
          }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    var conversation = await f.Db.Conversations.Select(x => x.Id).SingleAsync();
    var expected = await f
      .Db.ConversationMessages.AsNoTracking()
      .OrderByDescending(x => x.SentAt)
      .ThenByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Id)
      .Select(x => x.Id)
      .ToListAsync();

    var seen = new List<Guid>();
    MessageCursor? next = null;
    var pages = 0;
    do
    {
      var page = await PageAsync(f, me, conversation, next);
      seen.AddRange(page.Messages.Select(x => x.Id));
      Assert.Equal(page.Older, page.Next is not null);
      next = page.Next;
      pages++;
    } while (next is not null);

    Assert.Equal(3, pages);
    Assert.Equal(expected, seen);
  }

  // 1,030 messages, seven to each second: every page of at most fifty
  // continues exactly below the last, in the database's own order, and
  // costs the same number of statements however deep it is.
  [Fact]
  public async Task AThousandMessagesArePagedInBoundedSteps()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    f.Db.ChangeTracker.Clear();
    await new Application.Features.Messaging.Services.InboxRecorder(
      f.Db,
      TimeProvider.System
    ).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        .. Enumerable
          .Range(0, 1030)
          .Select(i => new DriverMessageInboundEvent(
            Phone,
            Start.AddSeconds(i / 7)
          )
          {
            ProviderMessageId = $"wamid.many.{i}",
            Text = $"message {i}",
          }),
      ],
      default
    );
    await f.Db.SaveChangesAsync();
    var conversation = await f.Db.Conversations.Select(x => x.Id).SingleAsync();
    var expected = await f
      .Db.ConversationMessages.AsNoTracking()
      .OrderByDescending(x => x.SentAt)
      .ThenByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Id)
      .Select(x => x.Id)
      .ToListAsync();

    var seen = new List<Guid>();
    var statements = new List<int>();
    MessageCursor? next = null;
    do
    {
      var before = f.Counter.Reads;
      var page = await PageAsync(f, me, conversation, next);
      statements.Add(f.Counter.Reads - before);
      Assert.InRange(page.Messages.Count, 1, 50);
      seen.AddRange(page.Messages.Select(x => x.Id));
      next = page.Next;
    } while (next is not null);

    Assert.Equal(21, statements.Count);
    Assert.Equal(expected, seen);
    Assert.Single(statements.Skip(1).Distinct());
  }

  // Sixty messages, all read. One arrives late with a time older than all
  // of them: the first page does not show it, so marking what it showed
  // leaves it unread; the older page that shows it lets it be read.
  [Fact]
  public async Task ALateMessageBelowThePageStaysUnreadUntilShown()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, Phone, 60);
    var opened = await PageAsync(f, me, conversation, null);
    await ReadAsync(f, me, conversation, opened.ReadThrough);
    Assert.Equal(0, (await InboxAsync(f, me)).Single().Unread);

    await RecordAsync(f, Phone, Start.AddHours(-1), "late");
    var late = await RevisionAsync(f, "late");
    var first = await PageAsync(f, me, conversation, null);
    await ReadAsync(f, me, conversation, first.ReadThrough);

    Assert.DoesNotContain(first.Messages, x => x.Body == "late");
    Assert.True(first.ReadThrough < late);
    Assert.Equal(1, (await InboxAsync(f, me)).Single().Unread);

    var older = await PageAsync(
      f,
      me,
      conversation,
      first.Next,
      first.Summary.Revision
    );
    await ReadAsync(f, me, conversation, older.ReadThrough);

    Assert.Contains(older.Messages, x => x.Body == "late");
    Assert.Equal(0, (await InboxAsync(f, me)).Single().Unread);
  }

  // Two pages shown and read; a message then arrives late with a time
  // inside the second, which the reader will not see until they read the
  // thread again. The third page must not let the marker pass it, and no
  // page repeats a message.
  [Fact]
  public async Task ALateMessageAmongPagesAlreadyShownIsNotMarkedRead()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var conversation = await ReceiveAsync(f, Phone, 120);
    var first = await PageAsync(f, me, conversation, null);
    var second = await PageAsync(
      f,
      me,
      conversation,
      first.Next,
      first.Summary.Revision
    );
    await ReadAsync(f, me, conversation, second.ReadThrough);

    await RecordAsync(f, Phone, Start.AddMinutes(45.5), "late");
    var late = await RevisionAsync(f, "late");
    var third = await PageAsync(
      f,
      me,
      conversation,
      second.Next,
      first.Summary.Revision
    );
    await ReadAsync(f, me, conversation, third.ReadThrough);

    Assert.True(third.ReadThrough < late);
    Assert.Equal(1, (await InboxAsync(f, me)).Single().Unread);
    var shown = first
      .Messages.Concat(second.Messages)
      .Concat(third.Messages)
      .ToList();
    Assert.Equal(120, shown.Count);
    Assert.Equal(120, shown.Select(x => x.Id).Distinct().Count());
    Assert.DoesNotContain(shown, x => x.Body == "late");
  }

  private static async Task<ConversationView> PageAsync(
    DispatchSyncFixture f,
    Guid user,
    Guid conversation,
    MessageCursor? before,
    long? seen = null
  ) =>
    (
      await Handlers(f, user)
        .Handle(new GetConversationQuery(conversation, before, seen), default)
    ).Response!;

  private static Task<long> RevisionAsync(DispatchSyncFixture f, string id) =>
    f
      .Db.ConversationMessages.AsNoTracking()
      .Where(x => x.ProviderMessageId == $"wamid.{Phone}.{id}")
      .Select(x => x.ArrivedRevision)
      .SingleAsync();
}
