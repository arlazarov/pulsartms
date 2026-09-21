using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// Falling back to one road through every chosen station, and the checks
// that decide whether a road that came back may be used at all.
public sealed partial class FuelCheckedRouteSearch
{
  private async Task<FuelCheckedRouteResult> CheckWholeAsync(
    IReadOnlyList<List<FuelCandidate>> grouped,
    CancellationToken ct
  )
  {
    var waypoints = new List<FuelRouteWaypoint>
    {
      new(baseline.Legs[0].Points[0]),
    };
    for (var index = 0; index < stops.Count; index++)
    {
      waypoints.AddRange(
        grouped[index]
          .Select(fuel => new FuelRouteWaypoint(fuel.Station.Point, Fuel: fuel))
      );
      waypoints.Add(new(baseline.Legs[index].Points[^1], Stop: stops[index]));
    }
    ct.ThrowIfCancellationRequested();
    RoadChecks++;
    var raw = await routing.CalculateAsync(
      waypoints.Select(waypoint => waypoint.Point).ToArray(),
      profile,
      ct
    );
    ct.ThrowIfCancellationRequested();
    if (
      !ValidGeometry(raw, waypoints.Count - 1, out _, out _)
      || !RouteAnchoring.Matches(
        raw,
        waypoints.Select(waypoint => waypoint.Point).ToArray()
      )
    )
      return Reject(
        "The complete checked candidate has invalid geometry, timing or stop anchoring."
      );
    var collapsed = FuelRouteVariant.Collapse(raw, waypoints);
    var route = collapsed.Route;
    route.Points = [];
    route.Miles = route.Legs.Sum(leg => leg.Miles);
    route.Seconds = route.Legs.Sum(leg => leg.Seconds);
    if (
      route.Miles <= 0
      || !RouteAnchoring.Matches(
        route,
        new[] { baseline.Legs[0].Points[0] }
          .Concat(stops.Select(stop => stop.Point))
          .ToArray()
      )
      || !RouteAnchoring.Near(
        route.Legs[0].Points[0],
        baseline.Legs[0].Points[0],
        RouteAnchoring.ContinuityToleranceMiles
      )
      || route
        .Legs.Where(
          (leg, index) =>
            !RouteAnchoring.Near(
              leg.Points[^1],
              baseline.Legs[index].Points[^1],
              RouteAnchoring.ContinuityToleranceMiles
            )
        )
        .Any()
    )
      return Reject(
        "The complete checked candidate does not preserve the mandatory road anchors."
      );
    return new(route, collapsed.Stations);
  }

  private bool ValidGeometry(
    TruckRoute? route,
    int legCount,
    out int points,
    out double seconds
  )
  {
    points = 0;
    seconds = 0;
    if (
      route?.Legs is null
      || route.Points is null
      || route.Points.Count > maximumGeometryPoints
      || route.Warnings is null
    )
      return false;
    foreach (var leg in route.Legs)
    {
      if (
        leg?.Points is null
        || leg.Points.Count > maximumGeometryPoints - points
      )
        return false;
      points += leg.Points.Count;
    }
    return SavedRouteGeometry.Complete(route, legCount)
      && route.TryGetLegSeconds(out seconds)
      && Math.Abs(route.Miles - route.Legs.Sum(leg => leg.Miles)) <= .01;
  }

  private void Remove(int index)
  {
    if (recipes[index] is { } recipe)
      RetainedPoints -= recipe.Leg.Points.Count;
    recipes[index] = null;
  }

  private static FuelCheckedRouteResult Reject(string reason) =>
    new(null, [], reason);
}
