using System.Data.Common;
using Application.Features.Messaging.Background;
using Application.Features.Messaging.Services;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Tests.Support;

namespace Server.Tests.Messaging;

// The outbox runs Messaging's reconciliation of kept statuses a previous
// binary left (audit F27): once a minute per carrier, not on each
// five-second pass, and the status then reaches the reply.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class OutboxReconciliationTests
{
  [Fact]
  public async Task TheOutboxReconcilesKeptStatusesOnceAMinute()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var worker = f.Worker;
    var (conversation, last) = await f.ConversationAsync();
    var id = (
      await f.SendAsync(
        new(conversation, "On my way", Guid.NewGuid(), last, false)
      )
    )
      .Response!
      .Id;
    await worker.RunOnceAsync(default);
    var sent = await f
      .Db.ConversationMessages.AsNoTracking()
      .SingleAsync(x => x.Id == id);
    Assert.NotNull(sent.ProviderMessageId);
    // What the previous binary left: a failure kept for the id it saved.
    f.Db.PendingDeliveryStatuses.Add(
      new PendingDeliveryStatus
      {
        Id = Guid.NewGuid(),
        Channel = sent.Channel,
        BusinessNumberId = sent.BusinessNumberId!,
        ProviderMessageId = sent.ProviderMessageId!,
        Status = DriverMessageStatuses.Failed,
        At = f.Clock.GetUtcNow().UtcDateTime,
        ErrorCode = 131047,
        ReceivedAt = f.Clock.GetUtcNow().UtcDateTime,
      }
    );
    await f.Db.SaveChangesAsync();

    await worker.RunOnceAsync(default);
    Assert.Equal(sent.Status, await StatusAsync(f, id));

    f.Clock.Advance(OutboundMessageOperation.ReconcileEvery);
    await worker.RunOnceAsync(default);
    Assert.Equal(DriverMessageStatuses.Failed, await StatusAsync(f, id));
  }

  // Root: the reconciliation's failure does not stop the pass - the
  // queued reply is still sent - and the carrier is tried again after a
  // minute, not on every pass.
  [Fact]
  public async Task AFailingReconciliationDoesNotStopTheSends()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var failing = new FailingReconciliation();
    f.Interceptors.Add(failing);
    var worker = f.Worker;
    var (conversation, last) = await f.ConversationAsync();
    await f.SendAsync(
      new(conversation, "On my way", Guid.NewGuid(), last, false)
    );

    Assert.Equal(1, await worker.RunOnceAsync(default));
    Assert.Equal(0, await worker.RunOnceAsync(default));
    Assert.Equal(1, failing.Attempts);

    f.Clock.Advance(OutboundMessageOperation.ReconcileEvery);
    await worker.RunOnceAsync(default);
    Assert.Equal(2, failing.Attempts);
  }

  // Root: the outbox remembers a carrier only until its next
  // reconciliation is due, so a carrier that stops being served is
  // forgotten.
  [Fact]
  public async Task ACarrierNoLongerServedIsForgotten()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var worker = f.Worker;
    using (f.Company.As(Guid.NewGuid()))
      await worker.RunOnceAsync(default);
    await worker.RunOnceAsync(default);
    Assert.Equal(2, worker.Reconciling);

    f.Clock.Advance(OutboundMessageOperation.ReconcileEvery);
    await worker.RunOnceAsync(default);

    Assert.Equal(1, worker.Reconciling);
  }

  // Root: until this revision is released, a queued reply waits in the
  // outbox - not sent, not lost - and goes once an administrator releases
  // the revision.
  [Fact]
  public async Task AQueuedReplyWaitsUntilTheRevisionIsReleased()
  {
    await using var f = await ReplyFixture.CreateAsync();
    f.HoldOptions.RequireRelease = true;
    var worker = f.Worker;
    var (conversation, last) = await f.ConversationAsync();
    var id = (
      await f.SendAsync(
        new(conversation, "On my way", Guid.NewGuid(), last, false)
      )
    )
      .Response!
      .Id;

    Assert.Equal(0, await worker.RunOnceAsync(default));
    Assert.Equal(OutboundStates.Queued, await StatusAsync(f, id));
    Assert.Empty(f.Messaging.Sent);

    f.Db.SendReleases.Add(
      new SendRelease
      {
        Id = Guid.NewGuid(),
        Revision = "test",
        ReleasedAt = f.Clock.GetUtcNow().UtcDateTime,
        ReleasedBy = "admin",
      }
    );
    await f.Db.SaveChangesAsync();
    f.Clock.Advance(SendHold.Recheck);
    Assert.Equal(1, await worker.RunOnceAsync(default));
  }

  private static Task<string> StatusAsync(ReplyFixture f, Guid id)
  {
    f.Db.ChangeTracker.Clear();
    return f
      .Db.ConversationMessages.AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => x.Status)
      .SingleAsync();
  }

  // Fails the reconciliation's search for kept statuses.
  private sealed class FailingReconciliation : DbCommandInterceptor
  {
    public int Attempts { get; private set; }

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        command.CommandText.Contains("\"PendingDeliveryStatuses\"")
        && command.CommandText.Contains("GROUP BY")
      )
      {
        Attempts++;
        throw new InvalidOperationException("Reconciliation failed.");
      }
      return ValueTask.FromResult(result);
    }
  }
}
