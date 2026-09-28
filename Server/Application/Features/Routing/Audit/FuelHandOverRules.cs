using Application.Diagnostics.Consistency;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Audit;

// Fuel hand-overs over WhatsApp (FuelIssueSender, audit D6). The provider
// call and the database cannot commit together, so two states can outlive
// a stopped process; neither is repaired here.
//
// Uncertain: the latest attempt of a hand-over has no answer - unknown, or
// sending past Messaging's timeout. Whether the driver has the stops is a
// question for the driver; a dispatcher sends again only if not.
public sealed class FuelHandOverUncertainRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.fuel-handover-uncertain",
      1,
      "Routing: FuelIssueSender",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "A fuel hand-over whose last attempt has no answer is checked with "
        + "the driver.",
      "Ask the driver whether the fuel stops arrived; if not, send them "
        + "again from the fuel plan."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var stale = request.Now - DriverMessageProgress.SendingTimeout;
    var rows = await db
      .DriverMessages.AsNoTracking()
      .Where(x =>
        x.CompanyId == request.Company
        && x.VisitKeys != ""
        && (
          x.Status == DriverMessageStatuses.Unknown
          || x.Status == DriverMessageStatuses.Sending && x.StatusAt <= stale
        )
        && !db.DriverMessages.Any(later =>
          later.IdempotencyKey == x.IdempotencyKey && later.Attempt > x.Attempt
        )
        && (after == null || x.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.TruckId,
        x.DispatchId,
        x.Attempt,
        x.Status,
        x.StatusAt,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"attempt:{x.Attempt};status:{x.Status}",
            new Dictionary<string, string>
            {
              ["truckId"] = x.TruckId.ToString(),
              ["dispatchId"] = x.DispatchId.ToString(),
              ["attempt"] = x.Attempt.ToString(),
              ["status"] = x.Status,
              ["statusAt"] = x.StatusAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}

// Unrecorded: the provider took a hand-over, and no hand-over was recorded
// for the truck since - the process stopped between the acceptance and the
// record (audit F17). Fuel planning could then drop a stop the driver
// holds. Sending the plan again records it from the accepted attempt
// without a second message. A later hand-over of the same stops moves the
// record to its own message, so any record for the truck made after this
// attempt was created counts; past the grace window only.
public sealed class FuelHandOverUnrecordedRule(IAppDbContext db)
  : IConsistencyRule
{
  private static readonly string[] Taken =
  [
    DriverMessageStatuses.Accepted,
    DriverMessageStatuses.Sent,
    DriverMessageStatuses.Delivered,
    DriverMessageStatuses.Read,
  ];

  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.fuel-handover-unrecorded",
      1,
      "Routing: FuelIssueSender",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A fuel hand-over the provider took is recorded as the driver's.",
      "Open the load's fuel plan and send it again: the accepted message "
        + "is recorded without a second one."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var due = request.Now - request.PendingGrace;
    var rows = await db
      .DriverMessages.AsNoTracking()
      .Where(x =>
        x.CompanyId == request.Company
        && x.VisitKeys != ""
        && Taken.Contains(x.Status)
        && x.CreatedAt <= due
        && !db.FuelVisitSends.Any(sent =>
          sent.MessageId == x.Id
          || sent.TruckId == x.TruckId && sent.SentAt >= x.CreatedAt
        )
        && (after == null || x.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.TruckId,
        x.DispatchId,
        x.Attempt,
        x.Status,
        x.CreatedAt,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"attempt:{x.Attempt};status:{x.Status}",
            new Dictionary<string, string>
            {
              ["truckId"] = x.TruckId.ToString(),
              ["dispatchId"] = x.DispatchId.ToString(),
              ["attempt"] = x.Attempt.ToString(),
              ["status"] = x.Status,
              ["createdAt"] = x.CreatedAt.ToString("O"),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
