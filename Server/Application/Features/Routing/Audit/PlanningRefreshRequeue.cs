using Application.Diagnostics.Consistency;
using Application.Features.Routing.Interfaces;

namespace Application.Features.Routing.Audit;

// The owner's requeue for overdue refresh demand: the same version becomes
// available now instead of after its backoff. It never touches assignments
// or execution; the planning owner still applies its own budgets and guards.
public sealed class PlanningRefreshRequeue(IPlanningRefreshStore store)
  : IConsistencyRepair
{
  public string Rule => "routing.planning-refresh-overdue";
  public string Action => "requeue-planning-refresh";

  public async Task<string> RequestAsync(
    ConsistencyRepairRequest request,
    CancellationToken ct
  ) =>
    request.Evidence.TryGetValue("requestedVersion", out var text)
    && long.TryParse(text, out var version)
    && await store.RequeueAsync(request.EntityKey, version, request.Now, ct)
      ? ConsistencyRepairOutcome.Requested
      : ConsistencyRepairOutcome.Superseded;
}
