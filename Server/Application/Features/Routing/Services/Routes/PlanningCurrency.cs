using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

// Whether a load is the work its truck is on now, for a writer holding its
// own snapshot. PlanningWorkPolicy decides which candidate is passed; this
// reads the saved plans one at a time through the store's cache and stops
// at the first one not passed, so work after the current is never read.
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
      if (!PlanningWorkPolicy.IsPassed(work, candidate, saved, profile))
        return candidate.Work == new WorkIdentity(load.Id, load.ExecutionLegId);
    }
    return false;
  }
}
