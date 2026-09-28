using Application.Diagnostics.Consistency;
using Domain.Rules;

namespace Application.Features.Execution.Audit;

// CW2 of docs/architecture/current-work.md: the source says a load is
// closed while its accepted execution still plans or runs it (AMF1395's
// shape; loads 1403 and 1385 on September 27). Accepted execution decides
// the truck's work, so the source being ahead is a review for a
// dispatcher, not a completion - nothing here closes the leg.
public sealed class SourceClosedWorkOpenRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "execution.source-closed-work-open",
      1,
      "Execution: AcceptExecutionSourceChanges",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "A load closed at its source has no planned or active execution.",
      "Open the load in Dispatch: complete the execution if the work is "
        + "done, or review the source change if it is not."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(link =>
        link.CompanyId == request.Company
        && (
          link.ExecutionLeg.Status == "planned"
          || link.ExecutionLeg.Status == "active"
        )
        && db.Dispatches.Any(load =>
          load.Id == link.DispatchId
          && load.Status.ToLower() == LoadCompletion.ClosedStatus
        )
        && (after == null || link.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(link => link.Id)
      .Select(link => new
      {
        link.Id,
        link.DispatchId,
        link.ExecutionLegId,
        link.ExecutionLeg.Status,
        link.ExecutionLeg.Revision,
        link.ExecutionLeg.TruckId,
        LoadNumber = db
          .Dispatches.Where(load => load.Id == link.DispatchId)
          .Select(load => load.LoadNumber)
          .FirstOrDefault(),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"leg:{x.Revision};status:{x.Status};source:closed",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["loadNumber"] = x.LoadNumber.ToString(),
              ["executionLegId"] = x.ExecutionLegId.ToString(),
              ["legStatus"] = x.Status,
              ["legRevision"] = x.Revision.ToString(),
              ["truckId"] = x.TruckId.ToString(),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}

// CW3: a source change the import could not accept on its own waits for a
// dispatcher (DispatchSourceLink.ExecutionReviewReason). It is expected,
// so it is a review; the journal's first-seen time is its age, since the
// link keeps no time of its own. Closed and cancelled loads are left to
// their own rules.
public sealed class SourceReviewOpenRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "execution.source-review-open",
      1,
      "Execution: ExecutionImportAcceptance",
      ConsistencyCondition.Review,
      ConsistencySeverity.Warning,
      "A source change waiting for a dispatcher is reviewed, not left open.",
      "Open the load in Dispatch and accept or correct the source change."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .DispatchSourceLinks.AsNoTracking()
      .Where(link =>
        link.CompanyId == request.Company
        && link.ExecutionReviewReason != null
        && link.Dispatch.Status.ToLower() != LoadCompletion.ClosedStatus
        && !SourceWords.Cancelled.Contains(link.Dispatch.Status.ToLower())
        && (after == null || link.DispatchId.CompareTo(after.Value) > 0)
      )
      .OrderBy(link => link.DispatchId)
      .Select(link => new
      {
        link.DispatchId,
        link.Dispatch.LoadNumber,
        link.Dispatch.Status,
        link.AssignmentSignature,
        link.ExecutionReviewReason,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.DispatchId.ToString(),
            $"assignment:{x.AssignmentSignature}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["loadNumber"] = x.LoadNumber.ToString(),
              ["sourceStatus"] = x.Status,
              ["reason"] = x.ExecutionReviewReason!,
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
