using Application.Features.Messaging.Interfaces;
using Domain.Entities.Messaging;
using Microsoft.Extensions.Logging;

namespace Application.Features.Messaging.Services;

// Applies kept delivery statuses whose provider id a message already holds
// (EarlyDeliveryStatuses.Unapplied): the previous binary saved the id
// during a release without taking them, so an early failure never reached
// the message (audit F27). Each message is reconciled as a sender would
// reconcile it - in its own transaction, under that message's lock, so a
// webhook or a sender on the same id is ordered with it, and with no other
// lock held, so no cycle can form. Statuses only move forward; an expired
// one is discarded, not applied. At most BatchSize messages per round.
// A message whose reconciliation fails is logged, left kept and skipped
// until KeptStatusRetries lets it be tried again, so the others progress.
public sealed class KeptStatusReconciliation(
  IAppDbContext db,
  IDeliveryStatusLocks locks,
  EarlyDeliveryStatuses early,
  ICurrentCompany company,
  MessagingEvents events,
  IEnumerable<IDriverTextObserver> observers,
  KeptStatusRetries retries,
  TimeProvider clock,
  ILogger<KeptStatusReconciliation> logger
)
{
  public const int BatchSize = 20;

  // The number of messages whose status moved.
  public async Task<int> RunOnceAsync(CancellationToken ct)
  {
    var owner =
      company.Id ?? throw new InvalidOperationException("A company is needed.");
    var now = clock.GetUtcNow().UtcDateTime;
    var waiting = retries.Waiting(owner, now);
    var candidates = await EarlyDeliveryStatuses
      .Unapplied(db)
      .AsNoTracking()
      .GroupBy(x => new
      {
        x.Channel,
        x.BusinessNumberId,
        x.ProviderMessageId,
      })
      .Select(x => new
      {
        x.Key.Channel,
        x.Key.BusinessNumberId,
        x.Key.ProviderMessageId,
        First = x.Min(p => p.ReceivedAt),
      })
      .OrderBy(x => x.First)
      .Take(BatchSize + waiting.Count)
      .ToListAsync(ct);
    var due = candidates
      .Select(x => new KeptStatusRetries.Key(
        owner,
        x.Channel,
        x.BusinessNumberId,
        x.ProviderMessageId
      ))
      .Where(x => !waiting.Contains(x))
      .Take(BatchSize)
      .ToList();
    var moved = 0;
    var failed = 0;
    foreach (var key in due)
      try
      {
        if (await ReconcileAsync(key, ct))
          moved++;
        retries.Done(key);
      }
      catch (Exception ex) when (!ct.IsCancellationRequested)
      {
        failed++;
        retries.Failed(key, now);
        logger.LogWarning(
          ex,
          "Kept delivery status reconciliation failed for one message; "
            + "tried again after {RetryAfter}",
          KeptStatusRetries.RetryAfter
        );
      }
    if (due.Count > 0)
      logger.LogInformation(
        "Reconciled kept delivery statuses for {Messages} messages, "
          + "{Moved} moved, {Failed} failed",
        due.Count,
        moved,
        failed
      );
    return moved;
  }

  private async Task<bool> ReconcileAsync(
    KeptStatusRetries.Key key,
    CancellationToken ct
  )
  {
    var owner = key.Company;
    db.ChangeTracker.Clear();
    DriverMessage? text = null;
    Guid? conversation = null;
    long revision = 0;
    await using (var transaction = await db.Database.BeginTransactionAsync(ct))
    {
      await locks.LockAsync(
        owner,
        key.Channel,
        key.BusinessNumberId,
        key.ProviderMessageId,
        ct
      );
      var attempt = await db.DriverMessages.FirstOrDefaultAsync(
        x =>
          x.Channel == key.Channel
          && x.BusinessNumberId == key.BusinessNumberId
          && x.ProviderMessageId == key.ProviderMessageId,
        ct
      );
      if (attempt is not null)
      {
        var before = (attempt.Status, attempt.StatusAt);
        await early.ApplyAsync(attempt, ct);
        if ((attempt.Status, attempt.StatusAt) != before)
          text = attempt;
      }
      else if (
        await db.ConversationMessages.FirstOrDefaultAsync(
          x =>
            x.Channel == key.Channel
            && x.BusinessNumberId == key.BusinessNumberId
            && x.ProviderMessageId == key.ProviderMessageId,
          ct
        )
          is { } message
        && await early.ApplyAsync(message, ct)
      )
        conversation = message.ConversationId;
      await db.SaveChangesAsync(ct);
      if (conversation is { } changed)
        revision = await OutboxRecords.BumpAsync(db, changed, ct);
      await transaction.CommitAsync(ct);
    }
    // After the commit, as the webhook tells them.
    if (conversation is { } published)
      events.Publish(owner, new(published, revision));
    if (text is not null)
      foreach (var observer in observers)
        observer.Changed(owner, [text]);
    return text is not null || conversation is not null;
  }
}
