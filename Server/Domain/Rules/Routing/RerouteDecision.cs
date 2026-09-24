using Domain.Models.Fleet;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Whether a truck should be given a new road now.
//
// Three things ask for one. A truck that has not started and is away from
// its pickup is driving empty to it, and that road is not the planned one.
// A truck that has left its road - or driven past the stop it was heading
// for - and stayed off it long enough to mean it, provided it has moved a
// mile since the last new road and the cooldown has passed: a truck
// circling a yard must not be rerouted on every lap. And a dispatcher who
// asks.
//
// A departure is followed fix by fix, each new fix once and in order, so
// it is timed from the first fix that showed it rather than from the pass
// that first looked. Two fixes of their own confirm it: two the
// persistence time apart, or two that were each twice the threshold away,
// with no wait. One fix - however far off - never gives a truck a new
// road, and a moderate departure plus one far outlier is not a clear one.
// Freshness is judged at the pass's own time; a stale fix neither
// confirms nor clears. The truck is back on its road only well inside
// the threshold, so one driving along it at about that distance is not
// reset by every fix. Providers report no position accuracy, so the
// threshold is set far above GPS error and the hysteresis absorbs jitter.
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

  public sealed record Policy(
    double Miles,
    TimeSpan Persistence,
    TimeSpan Cooldown
  )
  {
    public const int Observations = 2;
    public double ReturnMiles => Miles * .6;
    public double ClearMiles => Miles * 2;
  }

  // Fixes: the truck's new positions since the last judgement, oldest
  // first, each newer than the one before. Also keeps the plan's record of
  // the departure, which is what makes it persistent rather than
  // momentary.
  public static Verdict Judge(
    RoutePlan plan,
    RouteWorkSnapshot load,
    TruckLocation? truck,
    IReadOnlyList<TruckLocation> fixes,
    Policy policy,
    DateTime now,
    bool forceReroute,
    RouteGeometry? geometry = null
  )
  {
    var progress = RouteProgressMeasure.Of(plan, truck, load, geometry, now);
    var fresh = progress is { LocationStale: false, Position: not null };
    var remainingStops = (plan.ReferenceStops ?? plan.Stops)
      .Where(x => !plan.Tracking.PassedStopIds.Contains(x.Id))
      .ToList();
    var next = remainingStops.FirstOrDefault();
    var nextSource = next is null
      ? null
      : load.Stops.FirstOrDefault(x => x.Id == next.Id);
    var deadheadToPickup =
      fresh
      && !plan.FromCurrentPosition
      && plan.Tracking.PassedStopIds.Count == 0
      && nextSource?.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
        == true
      && AwayFrom(next, progress.Position);
    var nextIndex = plan.Stops.FindIndex(x => x.Id == next?.Id);
    var nextMile = plan
      .Route.Legs.Take(
        Math.Max(0, nextIndex + (plan.FromCurrentPosition ? 1 : 0))
      )
      .Sum(x => x.Miles);
    foreach (var fix in fixes)
    {
      var at = RouteProgressMeasure.Of(plan, fix, load, geometry, now);
      if (at is { LocationStale: false, Position: { } position })
        Observe(
          plan.Tracking,
          fix.UpdatedAt,
          at.DistanceFromRouteMiles,
          nextIndex >= 0 && at.ProgressMiles > nextMile + 2,
          AwayFrom(next, position),
          policy
        );
    }
    var confirmed =
      fresh
      && !plan.Tracking.AllStopsPassed
      && plan.Tracking.OffRouteConfirmedAt is not null
      && AwayFrom(next, progress.Position);
    var cooldownPassed =
      plan.LastReroutedAt is null
      || plan.LastReroutedAt <= now - policy.Cooldown;
    var moved =
      plan.LastReroutePosition is null
      || progress.Position is not null
        && RouteGeometry.Distance(plan.LastReroutePosition, progress.Position)
          >= 1;
    var reroute =
      (
        deadheadToPickup
        || confirmed && cooldownPassed && moved
        || forceReroute
          && fresh
          && !plan.FromCurrentPosition
          && !plan.Tracking.AllStopsPassed
      )
      && remainingStops.Count > 0;
    return new(progress, remainingStops, reroute);
  }

  private static void Observe(
    RouteStopTracking tracking,
    DateTime at,
    double away,
    bool behind,
    bool awayFromStop,
    Policy policy
  )
  {
    if (
      tracking.AllStopsPassed
      || !awayFromStop
      || away < policy.ReturnMiles && !behind
    )
    {
      tracking.ClearDeviation();
      return;
    }
    if (away <= policy.Miles && !behind)
      return;
    tracking.OffRouteSince ??= at;
    if (tracking.OffRouteFixes < Policy.Observations)
      tracking.OffRouteFixes++;
    if (
      away >= policy.ClearMiles
      && tracking.OffRouteFarFixes < Policy.Observations
    )
      tracking.OffRouteFarFixes++;
    var lasting =
      tracking.OffRouteFixes >= Policy.Observations
      && at - tracking.OffRouteSince >= policy.Persistence;
    var clear = tracking.OffRouteFarFixes >= Policy.Observations;
    if (lasting || clear)
      tracking.OffRouteConfirmedAt ??= at;
  }

  private static bool AwayFrom(PlanStop? stop, RoutePoint? position) =>
    stop is not null
    && position is not null
    && RouteGeometry.Distance(position, stop.Point) > 1;
}
