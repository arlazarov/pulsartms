using Application.Features.Messaging.Interfaces;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Services;

// A provider's status can arrive before the sender has saved the provider
// id it names - the send's answer and the status race (audit F27). It was
// dropped, so an early "failed" left the message showing accepted. Such a
// status is kept, per carrier, channel, business number and provider id,
// and applied when the id is saved. Both sides take the same lock, so the
// one second always sees what the first committed: the webhook, finding
// no id under the lock, keeps the status; the sender, saving the id under
// the lock, applies what is kept. A status for an id that is never saved
// (a send whose answer never came, another system's message) is pruned
// after Keep, and a carrier keeps at most PerCompany.
public sealed class EarlyDeliveryStatuses(
  IAppDbContext db,
  IDeliveryStatusLocks locks,
  ICurrentCompany company,
  TimeProvider clock,
  int perCompany = EarlyDeliveryStatuses.PerCompany
)
{
  public static readonly TimeSpan Keep = TimeSpan.FromHours(1);
  public const int PerCompany = 1000;

  public sealed record Found(
    IReadOnlyList<DriverMessage> Attempts,
    IReadOnlyList<Guid> Conversations
  );

  // In the webhook's transaction, for the statuses no saved id matched when
  // it read. Under the lock each id is looked for again: saved meanwhile,
  // its statuses apply now; still unknown, they are kept.
  public async Task<Found> KeepAsync(
    string channel,
    string businessNumber,
    IReadOnlyCollection<DriverMessageStatusEvent> statuses,
    CancellationToken ct
  )
  {
    var owner = Owner();
    var now = clock.GetUtcNow().UtcDateTime;
    var attempts = new Dictionary<Guid, DriverMessage>();
    var conversations = new HashSet<Guid>();
    // Admission first, for the whole carrier, so the count below is this
    // webhook's alone until it commits; then message locks in one order.
    await locks.LockAdmissionAsync(owner, ct);
    await db
      .PendingDeliveryStatuses.Where(x => x.ReceivedAt < now - Keep)
      .ExecuteDeleteAsync(ct);
    var held = await db.PendingDeliveryStatuses.CountAsync(ct);
    foreach (
      var group in statuses
        .GroupBy(x => x.ProviderMessageId)
        .OrderBy(x => x.Key, StringComparer.Ordinal)
    )
    {
      var id = group.Key;
      var ordered = group.OrderBy(x => x.At).ToList();
      await locks.LockAsync(owner, channel, businessNumber, id, ct);
      var attempt = await db.DriverMessages.FirstOrDefaultAsync(
        x =>
          x.Channel == channel
          && x.BusinessNumberId == businessNumber
          && x.ProviderMessageId == id,
        ct
      );
      if (attempt is not null)
      {
        foreach (var status in ordered)
          Advance(attempt, status.Status, status.At, status.ErrorCode);
        attempts[attempt.Id] = attempt;
        continue;
      }
      var message = await db.ConversationMessages.FirstOrDefaultAsync(
        x =>
          x.Channel == channel
          && x.BusinessNumberId == businessNumber
          && x.ProviderMessageId == id,
        ct
      );
      if (message is not null)
      {
        foreach (var status in ordered)
          Advance(message, status.Status, status.At, status.ErrorCode);
        conversations.Add(message.ConversationId);
        continue;
      }
      var kept = await Pending(channel, businessNumber, id)
        .Select(x => x.Status)
        .ToListAsync(ct);
      foreach (
        var status in ordered
          .Where(x => !kept.Contains(x.Status))
          .DistinctBy(x => x.Status)
      )
      {
        if (held >= perCompany)
          break;
        db.PendingDeliveryStatuses.Add(
          new()
          {
            Id = Guid.NewGuid(),
            Channel = channel,
            BusinessNumberId = businessNumber,
            ProviderMessageId = id,
            Status = status.Status,
            At = status.At,
            ErrorCode = status.ErrorCode,
            ReceivedAt = now,
          }
        );
        held++;
      }
    }
    return new([.. attempts.Values], [.. conversations]);
  }

  // In the sender's transaction, the provider id just set on the tracked
  // attempt: what the provider already reported applies now.
  public async Task ApplyAsync(DriverMessage attempt, CancellationToken ct)
  {
    foreach (
      var status in await TakeAsync(
        attempt.Channel,
        attempt.BusinessNumberId,
        attempt.ProviderMessageId,
        ct
      )
    )
      Advance(attempt, status.Status, status.At, status.ErrorCode);
  }

  // The same for a dispatcher's reply; true when its status moved.
  public async Task<bool> ApplyAsync(
    ConversationMessage message,
    CancellationToken ct
  )
  {
    var moved = false;
    foreach (
      var status in await TakeAsync(
        message.Channel,
        message.BusinessNumberId,
        message.ProviderMessageId,
        ct
      )
    )
      moved |= Advance(message, status.Status, status.At, status.ErrorCode);
    return moved;
  }

  private async Task<IReadOnlyList<PendingDeliveryStatus>> TakeAsync(
    string channel,
    string? businessNumber,
    string? providerMessageId,
    CancellationToken ct
  )
  {
    if (businessNumber is null || providerMessageId is null)
      return [];
    await locks.LockAsync(
      Owner(),
      channel,
      businessNumber,
      providerMessageId,
      ct
    );
    var pending = await Pending(channel, businessNumber, providerMessageId)
      .OrderBy(x => x.At)
      .ToListAsync(ct);
    // All are taken; only those still within Keep are applied. Pruning runs
    // when a webhook keeps a status, so an id saved after Keep with none
    // received meanwhile would otherwise apply an expired one.
    db.PendingDeliveryStatuses.RemoveRange(pending);
    var since = clock.GetUtcNow().UtcDateTime - Keep;
    return [.. pending.Where(x => x.ReceivedAt >= since)];
  }

  private IQueryable<PendingDeliveryStatus> Pending(
    string channel,
    string businessNumber,
    string providerMessageId
  ) =>
    db.PendingDeliveryStatuses.Where(x =>
      x.Channel == channel
      && x.BusinessNumberId == businessNumber
      && x.ProviderMessageId == providerMessageId
    );

  // Statuses only move forward, whichever order they arrive in.
  private static bool Advance(
    DriverMessage attempt,
    string status,
    DateTime at,
    int? errorCode
  )
  {
    if (!DriverMessageProgress.Advances(attempt.Status, status))
      return false;
    attempt.Status = status;
    attempt.StatusAt = at;
    attempt.ErrorCode = errorCode;
    return true;
  }

  private static bool Advance(
    ConversationMessage message,
    string status,
    DateTime at,
    int? errorCode
  )
  {
    if (!DriverMessageProgress.Advances(message.Status, status))
      return false;
    message.Status = status;
    message.StatusAt = at;
    message.ErrorCode = errorCode;
    return true;
  }

  private Guid Owner() =>
    company.Id ?? throw new InvalidOperationException("A company is needed.");
}
