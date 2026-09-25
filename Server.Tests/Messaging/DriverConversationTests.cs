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
  public async Task DriversAreListedByTheNumberTheirMessagesGoTo()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var ann = await DriverAsync(f, "Ann Lee", "+15550000001");
    var bo = await DriverAsync(f, "Bo Diaz", "+15550000002");
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
    Assert.All(all.Drivers, x => Assert.Equal("whatsapp", x.Source));
    Assert.Equal(
      [bo],
      (await ListAsync(f, search: "0002")).Drivers.Select(x => x.Id)
    );
    Assert.Equal(
      [ann],
      (await ListAsync(f, search: "ann")).Drivers.Select(x => x.Id)
    );
    Assert.Equal(
      [ann],
      (await ListAsync(f, withoutConversation: true)).Drivers.Select(x => x.Id)
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
    // No WhatsApp number, and a phone that is not a valid number.
    var phoneOnly = await DriverAsync(f, "Cy", null, phone: "555-0003");
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

  // Without a WhatsApp number of their own a driver's messages go to their
  // phone; with one, to it. A stored WhatsApp number that is not valid is
  // shown as such and never falls back to the phone. A phone that is not a
  // valid number is no recipient at all.
  [Fact]
  public async Task ThePhoneFillsInOnlyForABlankWhatsAppNumber()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var phoneOnly = await DriverAsync(f, "Ann Phone", null, "+15550000011");
    var both = await DriverAsync(f, "Bo Both", "+15550000012", "+15550000013");
    var invalid = await DriverAsync(f, "Cy Invalid", null, "+15550000014");
    await f
      .Db.Drivers.Where(x => x.Id == invalid)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.WhatsAppPhone, "555-01"));
    await DriverAsync(f, "Di Raw Phone", null, "(555) 000-0015");

    var listed = await ListAsync(f);
    Assert.Equal(
      [
        (phoneOnly, "+15550000011", "phone"),
        (both, "+15550000012", "whatsapp"),
        (invalid, (string?)null, "invalidWhatsApp"),
      ],
      listed.Drivers.Select(x => (x.Id, x.Number, x.Source))
    );
    Assert.Equal(
      [phoneOnly],
      (await ListAsync(f, search: "0011")).Drivers.Select(x => x.Id)
    );

    await OpenAsync(f, phoneOnly);
    await OpenAsync(f, both);
    var refused = await Handler(f)
      .Handle(new OpenDriverConversationCommand(invalid), default);
    Assert.Equal(409, refused.StatusCode);
    Assert.Equal(
      ["+15550000011", "+15550000012"],
      (
        await f
          .Db.Conversations.AsNoTracking()
          .Select(x => x.Participant)
          .ToListAsync()
      ).Order()
    );
    Assert.Equal(
      [invalid],
      (await ListAsync(f, withoutConversation: true)).Drivers.Select(x => x.Id)
    );
  }

  // A driver's phone is edited after their conversation began: the history
  // stays with the number it was with, a message queued there still goes
  // there, and choosing the driver now opens a conversation for the new
  // number.
  [Fact]
  public async Task ANewNumberGetsANewConversationAndHistoryStays()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var driver = await DriverAsync(f, "Ann Phone", null, "+15550000021");
    var first = await OpenAsync(f, driver);
    f.Db.ConversationMessages.Add(
      new ConversationMessage
      {
        Id = Guid.NewGuid(),
        ConversationId = first,
        Channel = f.Messaging.Channel,
        BusinessNumberId = "123456",
        Direction = MessageDirections.Outbound,
        Kind = ConversationMessageKinds.Template,
        Body = "queued",
        IdempotencyKey = Guid.NewGuid(),
        Attempt = 1,
        Status = OutboundStates.Queued,
        StatusAt = DateTime.UtcNow,
        SentAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
      }
    );
    await f.Db.SaveChangesAsync();
    await f
      .Db.Drivers.Where(x => x.Id == driver)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.Phone, "+15550000022"));

    var second = await OpenAsync(f, driver);

    Assert.NotEqual(first, second);
    f.Db.ChangeTracker.Clear();
    var conversations = await f
      .Db.Conversations.AsNoTracking()
      .ToDictionaryAsync(x => x.Id);
    Assert.Equal(
      ("+15550000021", "+15550000022"),
      (conversations[first].Participant, conversations[second].Participant)
    );
    Assert.All(conversations.Values, x => Assert.Equal(driver, x.DriverId));
    Assert.Equal(
      first,
      (
        await f.Db.ConversationMessages.AsNoTracking().SingleAsync()
      ).ConversationId
    );
  }

  // A driver writes from the number their messages go to, phone or
  // WhatsApp number, and is linked; two drivers with that number, or a
  // driver whose own WhatsApp number is another, are not.
  [Fact]
  public async Task AnInboundMessageLinksTheDriverTheRuleNames()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var byPhone = await DriverAsync(f, "Ann Phone", null, "+15550000031");
    await DriverAsync(f, "Bo Elsewhere", "+15550000039", "+15550000032");
    await DriverAsync(f, "Cy Shared", null, "+15550000033");
    await DriverAsync(f, "Di Shared", "+15550000033");
    await DriverAsync(
      f,
      "Ed Other Carrier",
      null,
      "+15550000034",
      company: Guid.NewGuid()
    );

    foreach (var phone in new[] { "31", "32", "33", "34" })
      await f.InboundAsync($"+155500000{phone}", $"wamid.{phone}", "hi");

    var linked = await f
      .Db.Conversations.AsNoTracking()
      .ToDictionaryAsync(x => x.Participant, x => x.DriverId);
    Assert.Equal(byPhone, linked["+15550000031"]);
    Assert.Null(linked["+15550000032"]);
    Assert.Null(linked["+15550000033"]);
    Assert.Null(linked["+15550000034"]);
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
    DriverScope? scope = null,
    bool withoutConversation = false
  )
  {
    var result = await Handler(f, scope: scope)
      .Handle(
        new GetMessagingDriversQuery(
          search,
          after,
          InChosenGroup: scope is not null,
          WithoutConversation: withoutConversation
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
    var context = db ?? f.Db;
    return new(
      context,
      new ReplyFixture.Caller(who),
      new TestDriverScope(scope),
      f.Messaging,
      new ConversationOpener(
        context,
        f.Messaging,
        new TestCompany(),
        f.Events,
        f.Clock
      )
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
