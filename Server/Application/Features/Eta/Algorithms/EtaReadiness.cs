using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Algorithms;

// Why an arrival time cannot be given yet, in the words the dispatcher
// reads. The order is the order of what has to exist first: a road, an
// assignment that stands, a road that is still this truck's, a GPS fix that
// is on it, and the driver's hours.
public static class EtaReadiness
{
  public sealed record Wait(string Reason, bool RouteUpdatePending = false);

  public static Wait? Reason(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    EtaChainPlan? chain,
    EtaPlanningOptions planning,
    DateTime now
  )
  {
    var plan = state.Plan;
    if (plan is null)
      return new("ETA unavailable: waiting for a current route.");
    if (chain?.CurrentUnavailableReason is { } assignmentReason)
      return new(assignmentReason);
    if (plan.InputsChanged)
      return new("ETA unavailable: route update in progress.", true);
    if (
      state.Progress?.LocationStale != false
      || state.Progress.ProgressMiles is not { } progress
      || !double.IsFinite(progress)
      || progress < 0
    )
      return new("ETA unavailable: waiting for current GPS.");
    if (
      state.Progress.OffRoute
      && (
        !double.IsFinite(state.Progress.DistanceFromRouteMiles)
        || state.Progress.DistanceFromRouteMiles
          > planning.OffRouteEstimateMaxMiles
      )
    )
      return new(
        "ETA unavailable: off-route distance is too large; route update in progress.",
        true
      );
    if (
      clocks?.DriveMs is null
      || clocks.ShiftMs is null
      || clocks.CycleMs is null
      || clocks.BreakMs is null
      || clocks.UpdatedAt < now.AddMinutes(-3)
    )
      return new("ETA unavailable: waiting for driver HOS.");
    return null;
  }
}
