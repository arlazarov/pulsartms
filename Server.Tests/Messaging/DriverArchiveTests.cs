using Application.Features.Messaging.Queries;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Messaging.InboxScenario;

namespace Server.Tests.Messaging;

// Conversations follow their driver's own status (Driver.IsActive), never
// the messages': the plain list shows active drivers and numbers linked to
// no driver; Archive shows drivers no longer active; Unread and a search
// keep both, each summary saying its driver's status. A driver made active
// again is back in the plain list. No message, conversation or driver
// changes with any of this.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class DriverArchiveTests
{
  [Fact]
  public async Task InactiveDriversChatsAreArchivedAndComeBackWhenReactivated()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (me, _) = await UsersAsync(f);
    var ann = await DriverAsync(f, "Ann", "+15550000001", active: true);
    var bo = await DriverAsync(f, "Bo", "+15550000002", active: false);
    var annChat = await ReceiveAsync(f, "+15550000001", 1);
    var boChat = await ReceiveAsync(f, "+15550000002", 2);
    var unlinked = await ReceiveAsync(f, "+15550000003", 1);
    var messages = await f.Db.ConversationMessages.CountAsync();

    var plain = await ListAsync(f, me, new(false));
    var archive = await ListAsync(f, me, new(false, Archived: true));
    var unread = await ListAsync(f, me, new(true));
    var searched = await ListAsync(f, me, new(false, "bo"));

    Assert.Equal(
      new HashSet<(Guid, bool?)> { (annChat, true), (unlinked, null) },
      plain.Select(x => (x.Id, x.DriverActive)).ToHashSet()
    );
    Assert.Equal(
      [(boChat, (bool?)false)],
      archive.Select(x => (x.Id, x.DriverActive))
    );
    Assert.Equal(3, unread.Count);
    Assert.False(unread.Single(x => x.Id == boChat).DriverActive);
    Assert.Equal(
      [(boChat, (bool?)false)],
      searched.Select(x => (x.Id, x.DriverActive))
    );

    // The dispatcher's group: the same split inside it.
    var group = new DriverScope(Guid.NewGuid(), "West", [ann, bo], []);
    Assert.Equal(
      [annChat],
      (await ListAsync(f, me, new(false, InChosenGroup: true), group)).Select(
        x => x.Id
      )
    );
    Assert.Equal(
      [boChat],
      (
        await ListAsync(
          f,
          me,
          new(false, InChosenGroup: true, Archived: true),
          group
        )
      ).Select(x => x.Id)
    );

    await f
      .Db.Drivers.Where(x => x.Id == bo)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.IsActive, true));
    Assert.Contains(
      await ListAsync(f, me, new(false)),
      x => x.Id == boChat && x.DriverActive == true
    );
    Assert.Empty(await ListAsync(f, me, new(false, Archived: true)));
    Assert.Equal(messages, await f.Db.ConversationMessages.CountAsync());
    Assert.Equal(3, await f.Db.Conversations.CountAsync());
  }

  private static async Task<IReadOnlyList<ConversationSummary>> ListAsync(
    DispatchSyncFixture f,
    Guid user,
    GetInboxQuery query,
    DriverScope? scope = null
  ) =>
    (await Handlers(f, user, scope).Handle(query, default))
      .Response!
      .Conversations;

  private static async Task<Guid> DriverAsync(
    DispatchSyncFixture f,
    string name,
    string phone,
    bool active
  )
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = name,
      Name = name,
      IsActive = active,
      WhatsAppPhone = phone,
    };
    f.Db.Drivers.Add(driver);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return driver.Id;
  }
}
