using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class RouteMovementRecorder
{
  public const int MaximumObservations = 64;

  public static void Observe(
    RoutePlan plan,
    RouteGeometry geometry,
    long revision,
    RouteObservation observation
  )
  {
    if (!observation.Point.IsValid || plan.Tracking.AllStopsPassed)
      return;
    var open = plan.Tracking.Movement;
    var last = open?.Observations.LastOrDefault();
    if (last is not null && observation.At <= last.At)
      return;
    var gap =
      last is not null
      && (
        observation.At - last.At > TimeSpan.FromMinutes(1)
        || RouteGeometry.Distance(last.Point, observation.Point)
          > (observation.At - last.At).TotalHours * 100 + 1
      );
    if (gap)
    {
      Close(plan);
      plan.CompletedMovement.Add(
        new()
        {
          TruckId = plan.TruckId,
          NextStopId = plan.Tracking.NextStopId,
          GeometryRevision = revision,
          Kind = "Gap",
          Observations = [last!, observation],
        }
      );
      open = null;
    }
    if (
      open is not null
      && (
        open.TruckId != plan.TruckId
        || open.GeometryRevision != revision
        || open.NextStopId != plan.Tracking.NextStopId
      )
    )
    {
      Close(plan);
      open = null;
    }
    var match = geometry.Match(observation.Point);
    var second = geometry.MatchAfter(observation.Point, match.Along + .1);
    var ambiguous = second.Along > match.Along + .1 && second.Away < .03;
    var forward =
      open?.ToMiles is not { } before
      || match.Along >= before - .02
        && (
          last is null
          || match.Along - before
            <= (observation.At - last.At).TotalHours * 100 + 1
        );
    var kind =
      revision > 0 && match.Away <= .03 && !ambiguous && forward
        ? "RouteMatchedEstimate"
        : "ObservedDeviation";
    if (open is not null && open.Kind != kind)
    {
      Close(plan);
      open = null;
    }
    if (open is null)
    {
      open = new()
      {
        TruckId = plan.TruckId,
        NextStopId = plan.Tracking.NextStopId,
        GeometryRevision = revision,
        Kind = kind,
        FromMiles = kind == "RouteMatchedEstimate" ? match.Along : null,
        Observations = [observation],
      };
      plan.Tracking.Movement = open;
    }
    else if (
      (
        kind == "RouteMatchedEstimate"
        || open.Observations[^1].Point == observation.Point
      )
      && open.Observations.Count == 2
    )
      open.Observations[1] = observation;
    else
      open.Observations.Add(observation);
    open.ToMiles =
      kind == "RouteMatchedEstimate"
        ? Math.Max(open.ToMiles ?? match.Along, match.Along)
        : null;
    if (open.Observations.Count >= MaximumObservations)
    {
      Close(plan);
      plan.Tracking.Movement = new()
      {
        TruckId = plan.TruckId,
        NextStopId = plan.Tracking.NextStopId,
        GeometryRevision = revision,
        Kind = kind,
        Observations = [observation],
      };
    }
  }

  public static void Close(RoutePlan plan)
  {
    if (plan.Tracking.Movement is not { } open)
      return;
    if (open.Kind == "ObservedDeviation" && open.Observations.Count > 2)
    {
      var kept = new HashSet<RoutePoint>(
        DisplayRouteGeometry.Simplify(
          open.Observations.Select(x => x.Point).ToList(),
          20
        ),
        ReferenceEqualityComparer.Instance
      );
      open.Observations = open
        .Observations.Where(x => kept.Contains(x.Point))
        .ToList();
    }
    plan.CompletedMovement.Add(open);
    plan.Tracking.Movement = null;
  }
}
