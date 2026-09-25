using Application.Diagnostics.Consistency;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;

namespace Application.Features.Messaging.Audit;

// The outbox sends a queued reply within seconds of it being queued, and
// turns a "sending" reply whose lease ran out into "unknown" on its next
// pass. A reply still queued longer than the pending grace after it was
// queued - even one taken again and again, whose lease keeps moving - or
// still sending that long after its lease, means the outbox is not running
// for the company or fails on the reply: the driver is waiting on a
// message that is not moving. Read only; the outbox owns every change,
// and a reply already with the provider is never sent again from here.
public sealed class OutboundOverdueRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "messaging.outbound-overdue",
      1,
      "Messaging: OutboundMessageOperation",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A queued or sending reply leaves that state soon after it is due.",
      "Check that the outbound message worker runs for the company and "
        + "its log for this message; the next pass settles it."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var overdue = request.Now - request.PendingGrace;
    var rows = await db
      .ConversationMessages.AsNoTracking()
      .Where(m =>
        m.CompanyId == request.Company
        && m.Direction == MessageDirections.Outbound
        && (after == null || m.Id.CompareTo(after.Value) > 0)
        && (
          (m.Status == OutboundStates.Queued && m.StatusAt < overdue)
          || (
            m.Status == DriverMessageStatuses.Sending && m.LeaseUntil < overdue
          )
        )
      )
      .OrderBy(m => m.Id)
      .Select(m => new
      {
        m.Id,
        m.ConversationId,
        m.Status,
        m.StatusAt,
        m.LeaseUntil,
        m.Attempt,
        m.Fence,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"status:{x.Status};fence:{x.Fence}",
            new Dictionary<string, string>
            {
              ["conversation"] = x.ConversationId.ToString(),
              ["status"] = x.Status,
              ["statusAt"] = x.StatusAt.ToString("O"),
              ["leaseUntil"] = x.LeaseUntil?.ToString("O") ?? "none",
              ["attempt"] = x.Attempt.ToString(),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
