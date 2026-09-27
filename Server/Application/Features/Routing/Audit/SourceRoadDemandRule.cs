using Application.Diagnostics.Consistency;
using Application.Features.Routing.Interfaces;

namespace Application.Features.Routing.Audit;

// A load's road is prepared in the background and retried with backoff, at
// most hourly, until its requested version completes (audit F23). A cap
// would only turn endless retries into a silent stop, so work still behind
// after the grace window is reported instead: the road is not converging.
// New inputs restart the wait, so a load whose inputs keep changing is not
// reported. The owner's store reads its own shared table for the serving
// company.
public sealed class SourceRoadDemandRule(ISourceRoadStore store)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "routing.source-road-overdue",
      1,
      "Routing: BaseRouteOperation",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "Requested road preparation for a load completes within the grace "
        + "window.",
      "Read the BaseRouteOperation wait line for this load: the step that "
        + "stops it and the inputs it waits on."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    var rows = await store.OverdueAsync(
      request.Now - request.PendingGrace,
      request.After is null ? null : Guid.Parse(request.After),
      request.Limit,
      ct
    );
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.DispatchId.ToString(),
            $"requested:{x.RequestedVersion};completed:{x.CompletedVersion}",
            new Dictionary<string, string>
            {
              ["dispatchStatus"] = x.DispatchStatus ?? "missing",
              ["requestedVersion"] = x.RequestedVersion.ToString(),
              ["completedVersion"] = x.CompletedVersion.ToString(),
              ["explicit"] = x.Explicit ? "true" : "false",
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
