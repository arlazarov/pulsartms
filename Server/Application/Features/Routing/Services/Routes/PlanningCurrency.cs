using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

// Whether every saved plan of the work a truck is doing was built from what
// that work says now. The question is a rule - PlanningWorkPolicy answers
// which work counts - but answering it means reading the saved plans, so it
// is asked here and not there.
public static class PlanningCurrency
{
  public static async Task<bool> IsCurrentAsync(
    TruckItinerarySnapshot work,
    RouteWorkSnapshot load,
    RoutePlanStore plans,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (!PlanningWorkPolicy.CanUseGps(load))
      return false;
    foreach (var candidate in PlanningWorkPolicy.Candidates(work))
    {
      var saved = await plans.ReadMetadataAsync(
        candidate.Work.DispatchId,
        ct,
        candidate.Work.ExecutionLegId
      );
      var resolved = PlanningWorkPolicy.Resolve(work, candidate);
      if (PlanningWorkPolicy.IsCompleted(saved, resolved, profile))
        continue;
      return candidate.Work == new WorkIdentity(load.Id, load.ExecutionLegId);
    }
    return false;
  }
}
