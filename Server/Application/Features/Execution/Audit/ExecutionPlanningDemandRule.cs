using Application.Diagnostics.Consistency;

namespace Application.Features.Execution.Audit;

// Execution planning changes are retried with backoff until they succeed.
// One still unfinished after the grace window is not converging. The rows
// are server-owned, so the company is filtered here explicitly.
public sealed class ExecutionPlanningDemandRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "execution.planning-change-overdue",
      1,
      "Execution: ExecutionPlanningOperation",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "An execution planning change completes within the grace window.",
      "Check ExecutionPlanningOperation logs for this change; its assignment "
        + "revision shows whether newer work superseded it."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var due = request.Now - request.PendingGrace;
    var rows = await db
      .ExecutionPlanningChanges.AsNoTracking()
      .Where(x =>
        x.CompanyId == request.Company
        && x.CompletedAt == null
        && x.RequestedAt <= due
        && (after == null || x.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.DispatchId,
        x.ExecutionLegId,
        x.AssignmentRevision,
        x.RequestedAt,
        x.AvailableAt,
        x.Attempts,
        LegRevision = db
          .ExecutionLegs.Where(leg => leg.Id == x.ExecutionLegId)
          .Select(leg => (long?)leg.Revision)
          .FirstOrDefault(),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"assignment:{x.AssignmentRevision};leg:{x.LegRevision}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["executionLegId"] = x.ExecutionLegId.ToString(),
              ["assignmentRevision"] = x.AssignmentRevision.ToString(),
              ["currentLegRevision"] = x.LegRevision?.ToString() ?? "missing",
              ["requestedAt"] = x.RequestedAt.ToString("O"),
              ["availableAt"] = x.AvailableAt.ToString("O"),
              ["attempts"] = x.Attempts.ToString(),
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
