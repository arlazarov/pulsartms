using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

public static class RouteViaGeometry
{
  public static (List<RoutePoint> Points, List<int> StopIndexes) Expand(
    IReadOnlyList<PlanStop> stops,
    IReadOnlyList<RouteViaPoint> via
  )
  {
    if (
      stops.Count is < 2 or > 49
      || via.Count > 20
      || stops.Count + via.Count > 50
      || via.Any(v =>
        v is null
        || v.Id == Guid.Empty
        || v.Point?.IsValid != true
        || string.IsNullOrWhiteSpace(v.Label)
        || v.Label.Length > 200
        || !stops.Skip(1).Any(s => s.Id == v.BeforeStopId)
      )
      || via.Select(v => v.Id).Distinct().Count() != via.Count
    )
      throw new RoutePlanningException(
        "Choose up to 20 valid via points between load stops (50 locations total)."
      );
    List<RoutePoint> points = [];
    List<int> indexes = [];
    foreach (var stop in stops)
    {
      points.AddRange(
        via.Where(v => v.BeforeStopId == stop.Id).Select(v => v.Point)
      );
      indexes.Add(points.Count);
      points.Add(stop.Point);
    }
    return (points, indexes);
  }

  public static TruckRoute Collapse(
    TruckRoute route,
    IReadOnlyList<int> indexes
  )
  {
    if (!SavedRouteGeometry.Complete(route, indexes[^1]))
      throw new RoutePlanningException("Incomplete route preview.");
    var legs = Enumerable
      .Range(1, indexes.Count - 1)
      .Select(i =>
        JoinLegs(
          route
            .Legs.Skip(indexes[i - 1])
            .Take(indexes[i] - indexes[i - 1])
            .ToList()
        )
      )
      .ToList();
    return Join(legs, route.Warnings, route.CalculatedAt);
  }

  public static RouteLeg JoinLegs(IReadOnlyList<RouteLeg> legs) =>
    new(
      legs.Sum(l => l.Miles),
      legs.Sum(l => l.Seconds),
      legs.SelectMany((l, i) => i == 0 ? l.Points : l.Points.Skip(1)).ToList()
    );

  public static RouteLeg Remaining(RouteLeg leg, RoutePoint position)
  {
    var geometry = new RouteGeometry(new() { Legs = [leg] });
    var match = geometry.Match(position);
    var lengths = leg
      .Points.Zip(leg.Points.Skip(1))
      .Select(pair => RouteGeometry.Distance(pair.First, pair.Second))
      .ToList();
    var total = lengths.Sum();
    var passed = 0d;
    List<RoutePoint> points = [match.Point];
    for (var i = 0; i < lengths.Count; i++)
    {
      passed += total > 0 ? lengths[i] / total * leg.Miles : 0;
      if (passed > match.Along)
        points.Add(leg.Points[i + 1]);
    }
    if (points.Count == 1)
      points.Add(leg.Points[^1]);
    var fraction =
      leg.Miles > 0 ? Math.Clamp(1 - match.Along / leg.Miles, 0, 1) : 0;
    return new(leg.Miles * fraction, leg.Seconds * fraction, points);
  }

  public static TruckRoute Join(
    List<RouteLeg> legs,
    IEnumerable<string> warnings,
    DateTime calculatedAt
  ) =>
    new()
    {
      Legs = legs,
      Miles = legs.Sum(l => l.Miles),
      Seconds = legs.Sum(l => l.Seconds),
      Points = legs.SelectMany((l, i) => i == 0 ? l.Points : l.Points.Skip(1))
        .ToList(),
      Warnings = warnings.Distinct().ToList(),
      CalculatedAt = calculatedAt,
    };
}
