using Application.Diagnostics.Consistency;
using Application.Features.Messaging.Audit;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Messaging;

// A reply that should have gone and has not: queued long after it was
// due, or sending long after its lease, while the outbox would have taken
// or reaped it within seconds. Fresh, leased and finished replies are not
// findings, and a pass of the outbox clears what it can move.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class OutboundOverdueRuleTests
{
  private static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

  [Fact]
  public async Task OnlyRepliesStuckPastTheGraceAreFound()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var now = f.Clock.GetUtcNow().UtcDateTime;
    var fresh = await QueueAsync(f, conversation, last, "fresh");
    var stuck = await QueueAsync(f, conversation, last, "stuck");
    var leased = await QueueAsync(f, conversation, last, "leased");
    var sending = await QueueAsync(f, conversation, last, "sending");
    var sent = await QueueAsync(f, conversation, last, "sent");
    await SetAsync(f, stuck, OutboundStates.Queued, now.AddHours(-1), null);
    await SetAsync(
      f,
      leased,
      OutboundStates.Queued,
      now.AddHours(-1),
      now.AddMinutes(1)
    );
    await SetAsync(
      f,
      sending,
      DriverMessageStatuses.Sending,
      now.AddHours(-1),
      now.AddMinutes(-40)
    );
    await SetAsync(
      f,
      sent,
      DriverMessageStatuses.Delivered,
      now.AddHours(-1),
      null
    );

    var found = await ReadAsync(f, now);

    Assert.Equal(
      new[] { stuck, sending }.Order().Select(x => x.ToString()),
      found.Select(x => x.EntityKey)
    );
    Assert.DoesNotContain(fresh.ToString(), found.Select(x => x.EntityKey));

    // The outbox moves the stuck one and reaps the sending one.
    await f.Worker.RunOnceAsync(default);
    Assert.Empty(await ReadAsync(f, now));
  }

  private static async Task<Guid> QueueAsync(
    ReplyFixture f,
    Guid conversation,
    Guid last,
    string text
  ) =>
    (await f.SendAsync(new(conversation, text, Guid.NewGuid(), last, false)))
      .Response!
      .Id;

  private static Task SetAsync(
    ReplyFixture f,
    Guid id,
    string status,
    DateTime at,
    DateTime? lease
  ) =>
    f
      .Db.ConversationMessages.Where(x => x.Id == id)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(m => m.Status, status)
          .SetProperty(m => m.StatusAt, at)
          .SetProperty(m => m.LeaseUntil, lease)
      );

  private static async Task<IReadOnlyList<ConsistencyObservation>> ReadAsync(
    ReplyFixture f,
    DateTime now
  ) =>
    (
      await new OutboundOverdueRule(f.Db).ReadAsync(
        new ConsistencyPageRequest(Company.Amf, now, null, 10, Grace),
        default
      )
    ).Observed;
}
