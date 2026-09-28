using Application.Diagnostics.Consistency;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;

namespace Application.Features.Messaging.Audit;

// A message the provider accepted and has said nothing about since. The
// provider reports "sent" within seconds of accepting a message, so one
// still accepted past the grace most likely lost its notifications - a
// webhook that did not reach PulsR, a subscription delivering elsewhere,
// or a status dropped during a release (audit F27) - or the provider
// stalled. It is not a failure: the message may well have been delivered,
// and nothing here marks it so or sends it again. A review for the
// dispatcher, who can ask the driver. Texts recorded before the business
// number was kept are never moved by a status and are not findings.
public sealed class AcceptedWithoutStatusRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "messaging.accepted-without-status",
      1,
      "Messaging: DriverMessagingWebhookHandlers",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "The provider reports a status soon after accepting a message.",
      "Delivery is unknown, not failed: check that the carrier's webhook "
        + "receives statuses, and ask the driver if it matters."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var silent = request.Now - request.PendingGrace;
    var texts = db
      .DriverMessages.AsNoTracking()
      .Where(m =>
        m.CompanyId == request.Company
        && m.Status == DriverMessageStatuses.Accepted
        && m.BusinessNumberId != null
        && m.StatusAt < silent
      )
      .Select(m => new
      {
        m.Id,
        Kind = "text",
        m.Channel,
        m.StatusAt,
      });
    var replies = db
      .ConversationMessages.AsNoTracking()
      .Where(m =>
        m.CompanyId == request.Company
        && m.Direction == MessageDirections.Outbound
        && m.Status == DriverMessageStatuses.Accepted
        && m.StatusAt < silent
      )
      .Select(m => new
      {
        m.Id,
        Kind = "reply",
        m.Channel,
        m.StatusAt,
      });
    var rows = await texts
      .Concat(replies)
      .Where(x => after == null || x.Id.CompareTo(after.Value) > 0)
      .OrderBy(x => x.Id)
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"status:{DriverMessageStatuses.Accepted}",
            new Dictionary<string, string>
            {
              ["kind"] = x.Kind,
              ["channel"] = x.Channel,
              ["acceptedAt"] = x.StatusAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
