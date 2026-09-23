using Application.Diagnostics.Consistency;
using Application.Features.Routing.Interfaces;

namespace Application.Features.Routing.Audit;

// Planning refresh demand is retried with backoff until its requested
// version completes. Demand still behind after the grace window is planning
// that is not converging, whatever the display says. The owner's store reads
// its own server-owned table for the serving company.
public sealed class PlanningRefreshDemandRule(IPlanningRefreshStore store)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.planning-refresh-overdue",
      1,
      "Routing: PlanningRefreshOperation",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "Requested planning refresh completes within the grace window.",
      "Check PlanningRefreshOperation logs for this demand; compare its "
        + "assignment revision with the current leg revision."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    var rows = await store.OverdueAsync(
      request.Now - request.PendingGrace,
      request.After,
      request.Limit,
      ct
    );
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id,
            $"requested:{x.RequestedVersion};completed:{x.CompletedVersion};"
              + $"assignment:{x.AssignmentRevision};"
              + $"leg:{x.CurrentLegRevision}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["executionLegId"] = x.ExecutionLegId?.ToString() ?? "none",
              ["assignmentRevision"] = x.AssignmentRevision.ToString(),
              ["currentLegRevision"] =
                x.CurrentLegRevision?.ToString() ?? "missing",
              ["requestedVersion"] = x.RequestedVersion.ToString(),
              ["completedVersion"] = x.CompletedVersion.ToString(),
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
