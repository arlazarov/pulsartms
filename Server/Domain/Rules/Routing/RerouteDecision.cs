using Domain.Models.Fleet;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Whether a truck should be given a new road now.
//
// Three things ask for one. A truck that has not started and is away from
// its pickup is driving empty to it, and that road is not the planned one.
// A truck that has left its road - or driven past the stop it was heading
// for - and stayed off it long enough to mean it, provided it has moved a
// mile since the last new road and five minutes have passed: a truck
// circling a yard must not be rerouted on every lap. And a dispatcher who
// asks.
//
// None of it counts while the truck stands within a mile of its next stop:
// a yard is not on the road, and leaving one is not a deviation.
public static class RerouteDecision
{
  public sealed record Verdict(
    RouteProgress Progress,
    List<PlanStop> RemainingStops,
    bool Reroute
  );

  // Also keeps the plan's own record of when the truck left the road, which
  // is what makes a deviation persistent rather than momentary.
  public static Verdict Judge(
    RoutePlan plan,
    RouteWorkSnapshot load,
    TruckLocation? truck,
    double deviationMiles,
    double deviationSeconds,
    DateTime now,
    bool forceReroute
  )
  {
    var progress = RouteProgressMeasure.Of(plan, truck, load);
    var fresh = progress is { LocationStale: false, Position: not null };
    var remainingStops = (plan.ReferenceStops ?? plan.Stops)
      .Where(x => !plan.Tracking.PassedStopIds.Contains(x.Id))
      .ToList();
    var next = remainingStops.FirstOrDefault();
    var awayFromStop =
      next is not null
      && progress.Position is not null
      && RouteGeometry.Distance(progress.Position, next.Point) > 1;
    var nextSource = next is null
      ? null
      : load.Stops.FirstOrDefault(x => x.Id == next.Id);
    var deadheadToPickup =
      fresh
      && !plan.FromCurrentPosition
      && plan.Tracking.PassedStopIds.Count == 0
      && nextSource?.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
        == true
      && awayFromStop;
    var nextIndex = plan.Stops.FindIndex(x => x.Id == next?.Id);
    var nextMile = plan
      .Route.Legs.Take(
        Math.Max(0, nextIndex + (plan.FromCurrentPosition ? 1 : 0))
      )
      .Sum(x => x.Miles);
    var pendingBehind = nextIndex >= 0 && progress.ProgressMiles > nextMile + 2;
    var off =
      fresh
      && !plan.Tracking.AllStopsPassed
      && (progress.DistanceFromRouteMiles > deviationMiles || pendingBehind)
      && awayFromStop;
    if (!off)
      plan.Tracking.OffRouteSince = null;
    else
      plan.Tracking.OffRouteSince ??= truck!.UpdatedAt;
    var persistentDeviation =
      off
      && truck!.UpdatedAt - plan.Tracking.OffRouteSince
        >= TimeSpan.FromSeconds(deviationSeconds);
    var cooldownPassed =
      plan.LastReroutedAt is null || plan.LastReroutedAt < now.AddMinutes(-5);
    var moved =
      plan.LastReroutePosition is null
      || progress.Position is not null
        && RouteGeometry.Distance(plan.LastReroutePosition, progress.Position)
          >= 1;
    var reroute =
      (
        deadheadToPickup
        || persistentDeviation && cooldownPassed && moved
        || forceReroute
          && fresh
          && !plan.FromCurrentPosition
          && !plan.Tracking.AllStopsPassed
      )
      && remainingStops.Count > 0;
    return new(progress, remainingStops, reroute);
  }
}
