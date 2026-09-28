using Application.Diagnostics.Consistency;
using Application.Features.Messaging.Services;

namespace Application.Features.Messaging.Audit;

// A delivery status kept because it arrived before its provider id was
// saved (audit F27) is taken, under the message's lock, by whichever
// Messaging writer saves the id. One still kept while a message under the
// same carrier, channel and business number already holds the id was
// never applied: the id was saved by a writer that does not take kept
// statuses - the previous binary during a release, which knows neither
// the lock nor the table. KeptStatusReconciliation applies such statuses
// within a minute or so; a finding that stays means it is not running for
// the carrier or fails. Read only.
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
      "Check that the outbound message worker runs for the company and "
        + "its log; its next reconciliation applies the kept status."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await EarlyDeliveryStatuses
      .Unapplied(db)
      .AsNoTracking()
      .Where(p =>
        p.CompanyId == request.Company
        && (after == null || p.Id.CompareTo(after.Value) > 0)
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
