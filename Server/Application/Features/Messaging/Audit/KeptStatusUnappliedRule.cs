using Application.Diagnostics.Consistency;

namespace Application.Features.Messaging.Audit;

// A delivery status kept because it arrived before its provider id was
// saved (audit F27) is taken, under the message's lock, by whichever
// Messaging writer saves the id. One still kept while a message under the
// same carrier, channel and business number already holds the id was
// never applied: the id was saved by a writer that does not take kept
// statuses - the previous binary during a release, which knows neither
// the lock nor the table. The message then shows an older status than the
// provider reported, possibly accepted after a failure, and WhatsApp has
// no status query to recover it. Read only. A finding lasts while the row
// is kept: until a webhook prunes it, an hour after it was received.
public sealed class KeptStatusUnappliedRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "messaging.kept-status-unapplied",
      1,
      "Messaging: EarlyDeliveryStatuses",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A status kept before its provider id was saved is applied when the "
        + "id is saved.",
      "The provider's status did not reach the message; tell the "
        + "dispatcher which message it was and what the provider reported."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .PendingDeliveryStatuses.AsNoTracking()
      .Where(p =>
        p.CompanyId == request.Company
        && (after == null || p.Id.CompareTo(after.Value) > 0)
        && (
          db.DriverMessages.Any(m =>
            m.CompanyId == p.CompanyId
            && m.Channel == p.Channel
            && m.BusinessNumberId == p.BusinessNumberId
            && m.ProviderMessageId == p.ProviderMessageId
          )
          || db.ConversationMessages.Any(m =>
            m.CompanyId == p.CompanyId
            && m.Channel == p.Channel
            && m.BusinessNumberId == p.BusinessNumberId
            && m.ProviderMessageId == p.ProviderMessageId
          )
        )
      )
      .OrderBy(p => p.Id)
      .Select(p => new
      {
        p.Id,
        p.Channel,
        p.Status,
        p.At,
        p.ErrorCode,
        p.ReceivedAt,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"status:{x.Status}",
            new Dictionary<string, string>
            {
              ["channel"] = x.Channel,
              ["status"] = x.Status,
              ["at"] = x.At.ToString("O"),
              ["errorCode"] = x.ErrorCode?.ToString() ?? "none",
              ["receivedAt"] = x.ReceivedAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
