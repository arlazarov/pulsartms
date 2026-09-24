using Application.Features.Messaging.Queries;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Messaging.InboxScenario;

namespace Server.Tests.Messaging;

// The history is shared and what is unread is each dispatcher's own. A
// dispatcher's chosen driver group narrows the inbox to its drivers'
// conversations - an unlinked one is under All - but not the unread
// notice, and never what anyone else has read.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class DriverGroupInboxTests
{
  [Fact]
  public async Task AGroupNarrowsTheListNotWhatIsUnread()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, colleague) = await UsersAsync(f);
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "west",
      Name = "West One",
      IsActive = true,
      WhatsAppPhone = "+15550000001",
    };
    f.Db.Drivers.Add(driver);
    await f.Db.SaveChangesAsync();
    var linked = await ReceiveAsync(f, "+15550000001", 2);
    var unlinked = await ReceiveAsync(f, "+15550000002", 1);
    var west = new DriverScope(Guid.NewGuid(), "West", [driver.Id], []);

    var inbox = (
      await Handlers(f, me, west)
        .Handle(new GetInboxQuery(false, InChosenGroup: true), default)
    ).Response!;
    var notice = (
      await Handlers(f, me, west).Handle(new GetUnreadNoticeQuery(), default)
    ).Response!;

    Assert.Equal([linked], inbox.Conversations.Select(x => x.Id));
    Assert.Equal(2, notice.Conversations);
    Assert.Equal(
      2,
      (await Handlers(f, me, west).Handle(new GetInboxQuery(false), default))
        .Response!
        .Conversations
        .Count
    );
    Assert.Equal(
      new[] { linked, unlinked }.Order(),
      (await InboxAsync(f, me)).Select(x => x.Id).Order()
    );

    // Reading is each dispatcher's own: mine leaves my colleague's unread.
    await ReadAsync(f, me, linked, 99);
    Assert.Equal(
      0,
      (await InboxAsync(f, me)).Single(x => x.Id == linked).Unread
    );
    Assert.Equal(
      2,
      (await InboxAsync(f, colleague)).Single(x => x.Id == linked).Unread
    );
    Assert.Single(await f.Db.ConversationReads.AsNoTracking().ToListAsync());
  }
}
