using Application.Diagnostics.Consistency;
using Domain.Entities.Messaging;

namespace Application.Features.Routing.Audit;

// Unread and notices compare a dispatcher's marker with the revision the
// conversation's latest driver message arrived at. A conversation whose
// recorded latest arrival is behind one of its own driver messages hides
// that message from every unread count and notice. The recorder writes
// both in one transaction, so any finding is a defect, not a delay. Read
// only; the conversation's next driver message sets it right.
public sealed class UnreadArrivalRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "messaging.unread-arrival-behind",
      1,
      "Routing: InboxRecorder",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A conversation's latest arrival revision covers its driver messages.",
      "Compare the conversation's LastInboundRevision with the "
        + "ArrivedRevision of its driver messages; check InboxRecorder."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .Conversations.AsNoTracking()
      .Where(c =>
        c.CompanyId == request.Company
        && (after == null || c.Id.CompareTo(after.Value) > 0)
        && db.ConversationMessages.Any(m =>
          m.ConversationId == c.Id
          && m.Direction == MessageDirections.Inbound
          && m.ArrivedRevision > c.LastInboundRevision
        )
      )
      .OrderBy(c => c.Id)
      .Select(c => new
      {
        c.Id,
        c.Revision,
        c.LastInboundRevision,
        Arrived = db
          .ConversationMessages.Where(m =>
            m.ConversationId == c.Id && m.Direction == MessageDirections.Inbound
          )
          .Max(m => (long?)m.ArrivedRevision),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"latest:{x.LastInboundRevision};arrived:{x.Arrived}",
            new Dictionary<string, string>
            {
              ["revision"] = x.Revision.ToString(),
              ["lastInboundRevision"] = x.LastInboundRevision.ToString(),
              ["arrivedRevision"] = x.Arrived?.ToString() ?? "none",
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
