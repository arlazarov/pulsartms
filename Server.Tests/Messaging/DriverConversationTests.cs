using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Messaging;

// A dispatcher starts a chat by choosing a driver with a WhatsApp number
// of their own. Choosing opens the driver's conversation on the number the
// company sends from, or creates it once; it sends nothing, opens no reply
// window and leaves every dispatcher's unread as it was.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class DriverConversationTests
{
  [Fact]
  public async Task ChoosingADriverCreatesOneQuietConversation()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var driver = await DriverAsync(f, "Ann Lee", "+15550000001");

    var first = await OpenAsync(f, driver);
    var again = await OpenAsync(f, driver);

    Assert.Equal(first, again);
    var conversation = await f.Db.Conversations.AsNoTracking().SingleAsync();
    Assert.Equal(
      (driver, "+15550000001", "123456"),
      (
        conversation.DriverId,
        conversation.Participant,
        conversation.BusinessNumberId
      )
    );
    Assert.Null(conversation.LastInboundAt);
    Assert.Equal(0, conversation.LastInboundRevision);
    Assert.Empty(await f.Db.ConversationMessages.AsNoTracking().ToListAsync());
    Assert.Empty(f.Messaging.Sent);
    var summary = Assert.Single(await InboxAsync(f));
    Assert.Equal(
      (first, 0, false),
      (summary.Id, summary.Unread, summary.WindowOpen)
    );
    Assert.Equal(0, (await NoticeAsync(f)).Conversations);

    // A text needs the driver's window; the first message is a template.
    var text = await f.SendAsync(
      new(first, "Hello", Guid.NewGuid(), null, false)
    );
    Assert.Equal(409, text.StatusCode);
    Assert.Empty(await f.Db.ConversationMessages.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task ADriversExistingConversationIsOpenedAsItIs()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (existing, _) = await f.ConversationAsync("+15550000002");
    var before = await f
      .Db.Conversations.AsNoTracking()
      .SingleAsync(x => x.Id == existing);
    var driver = await DriverAsync(f, "Bo Diaz", "+15550000002");

    Assert.Equal(existing, await OpenAsync(f, driver));

    var after = await f
      .Db.Conversations.AsNoTracking()
      .SingleAsync(x => x.Id == existing);
    Assert.Equal(
      (before.Revision, before.LastInboundRevision, before.LastInboundAt),
      (after.Revision, after.LastInboundRevision, after.LastInboundAt)
    );
    Assert.Equal(1, Assert.Single(await InboxAsync(f)).Unread);
    Assert.Single(await f.Db.Conversations.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task OnlyAnExplicitWhatsAppNumberIsListedInTheChosenGroup()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var ann = await DriverAsync(f, "Ann Lee", "+15550000001");
    var bo = await DriverAsync(f, "Bo Diaz", "+15550000002");
    // An ordinary phone is never taken for a WhatsApp number.
    await DriverAsync(f, "Cy Phone Only", null, phone: "+15550000003");
    await DriverAsync(f, "Di Inactive", "+15550000004", active: false);
    await DriverAsync(
      f,
      "Ed Other Carrier",
      "+15550000005",
      company: Guid.NewGuid()
    );
    var opened = await OpenAsync(f, bo);

    var all = await ListAsync(f);
    Assert.True(all.Configured);
    Assert.Equal(
      [(ann, (Guid?)null), (bo, opened)],
      all.Drivers.Select(x => (x.Id, x.ConversationId))
    );
    Assert.Equal(
      [bo],
      (await ListAsync(f, search: "0002")).Drivers.Select(x => x.Id)
    );
    Assert.Equal(
      [ann],
      (await ListAsync(f, search: "ann")).Drivers.Select(x => x.Id)
    );
    var group = new DriverScope(Guid.NewGuid(), "West", [bo], []);
    Assert.Equal(
      [bo],
      (await ListAsync(f, scope: group)).Drivers.Select(x => x.Id)
    );
  }

  [Fact]
  public async Task TheListIsPagedByNameWithoutRepeats()
  {
    await using var f = await ReplyFixture.CreateAsync();
    for (var i = 0; i < DriverConversations.PageSize + 2; i++)
      await DriverAsync(f, $"Driver {i:000}", $"+1555000{i:0000}");

    var first = await ListAsync(f);
    var second = await ListAsync(f, after: first.Next);

    Assert.True(first.More);
    Assert.Equal(DriverConversations.PageSize, first.Drivers.Count);
    Assert.False(second.More);
    Assert.Equal(2, second.Drivers.Count);
    Assert.Empty(
      first
        .Drivers.Select(x => x.Id)
        .Intersect(second.Drivers.Select(x => x.Id))
    );
  }

  [Fact]
  public async Task NothingIsCreatedWithoutANumberToSendFromOrToOrAccess()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var ann = await DriverAsync(f, "Ann Lee", "+15550000001");
    var phoneOnly = await DriverAsync(f, "Cy", null, phone: "+15550000003");
    var other = await DriverAsync(
      f,
      "Ed Other Carrier",
      "+15550000005",
      company: Guid.NewGuid()
    );

    Assert.Equal(
      409,
      (
        await Handler(f)
          .Handle(new OpenDriverConversationCommand(phoneOnly), default)
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await Handler(f)
          .Handle(new OpenDriverConversationCommand(other), default)
      ).StatusCode
    );
    Assert.Equal(
      403,
      (
        await Handler(f, "nobody")
          .Handle(new OpenDriverConversationCommand(ann), default)
      ).StatusCode
    );
    f.Messaging.Configured = false;
    Assert.Equal(
      409,
      (
        await Handler(f).Handle(new OpenDriverConversationCommand(ann), default)
      ).StatusCode
    );
    Assert.False((await ListAsync(f)).Configured);
    Assert.Empty(await f.Db.Conversations.AsNoTracking().ToListAsync());
  }

  // Two dispatchers choose the driver at once: the other's conversation is
  // committed after this one looked for it and before this one's insert.
  [Fact]
  public async Task ChoosingTogetherMakesOneConversation()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var driver = await DriverAsync(f, "Ann Lee", "+15550000001");
    var theirs = Guid.NewGuid();
    await using var mine = f.Context(
      new BeforeSave(async () =>
      {
        await using var other = f.Context();
        other.Conversations.Add(
          new Conversation
          {
            Id = theirs,
            Channel = f.Messaging.Channel,
            BusinessNumberId = "123456",
            Participant = "+15550000001",
            DriverId = driver,
            LastMessageAt = DateTime.UtcNow,
            Revision = 1,
          }
        );
        await other.SaveChangesAsync();
      })
    );

    var opened = await Handler(f, db: mine)
      .Handle(new OpenDriverConversationCommand(driver), default);

    Assert.Equal(theirs, opened.Response);
    f.Db.ChangeTracker.Clear();
    Assert.Equal(
      [theirs],
      await f.Db.Conversations.AsNoTracking().Select(x => x.Id).ToListAsync()
    );
  }

  private static async Task<Guid> OpenAsync(ReplyFixture f, Guid driver)
  {
    var result = await Handler(f)
      .Handle(new OpenDriverConversationCommand(driver), default);
    Assert.True(result.Success, $"status {result.StatusCode}");
    return result.Response;
  }

  private static async Task<MessagingDriversView> ListAsync(
    ReplyFixture f,
    string? search = null,
    MessagingDriverCursor? after = null,
    DriverScope? scope = null
  )
  {
    var result = await Handler(f, scope: scope)
      .Handle(
        new GetMessagingDriversQuery(
          search,
          after,
          InChosenGroup: scope is not null
        ),
        default
      );
    Assert.True(result.Success, $"status {result.StatusCode}");
    return result.Response!;
  }

  private static DriverConversations Handler(
    ReplyFixture f,
    string who = "me",
    AppDbContext? db = null,
    DriverScope? scope = null
  )
  {
    f.Db.ChangeTracker.Clear();
    return new(
      db ?? f.Db,
      new ReplyFixture.Caller(who),
      new TestCompany(),
      new TestDriverScope(scope),
      f.Messaging,
      f.Events,
      f.Clock
    );
  }

  private static InboxHandlers Inbox(ReplyFixture f) =>
    new(
      f.Db,
      new ReplyFixture.Caller("me"),
      new ConversationReadMarkers(f.Db),
      new TestDriverScope(),
      f.Clock
    );

  private static async Task<IReadOnlyList<ConversationSummary>> InboxAsync(
    ReplyFixture f
  )
  {
    f.Db.ChangeTracker.Clear();
    return (await Inbox(f).Handle(new GetInboxQuery(false), default))
      .Response!
      .Conversations;
  }

  private static async Task<UnreadNotice> NoticeAsync(ReplyFixture f)
  {
    f.Db.ChangeTracker.Clear();
    return (
      await Inbox(f).Handle(new GetUnreadNoticeQuery(), default)
    ).Response!;
  }

  private static async Task<Guid> DriverAsync(
    ReplyFixture f,
    string name,
    string? whatsApp,
    string? phone = null,
    bool active = true,
    Guid? company = null
  )
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = name,
      Name = name,
      IsActive = active,
      Phone = phone,
      WhatsAppPhone = whatsApp,
    };
    if (company is { } other)
      driver.CompanyId = other;
    f.Db.Drivers.Add(driver);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return driver.Id;
  }

  private sealed class BeforeSave(Func<Task> action) : SaveChangesInterceptor
  {
    private Func<Task>? pending = action;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken ct = default
    )
    {
      if (pending is { } run)
      {
        pending = null;
        await run();
      }
      return result;
    }
  }
}
