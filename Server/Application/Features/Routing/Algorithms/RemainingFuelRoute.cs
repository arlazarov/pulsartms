using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class RemainingFuelRoute
{
  public static TruckRoute? TryRead(RoutePlan plan, IReadOnlyList<PlanStop> remaining, RoutePoint position,
    double maximumMatchMiles = .05)
  {
    if (!double.IsFinite(maximumMatchMiles) || maximumMatchMiles < 0)
      throw new ArgumentOutOfRangeException(nameof(maximumMatchMiles));
    if (!plan.FromCurrentPosition || plan.InputsChanged || remaining.Count == 0
      || plan.Route.Legs.Count != plan.Stops.Count) return null;
    var index = plan.Stops.FindIndex(x => x.Id == remaining[0].Id);
    if (index < 0 || !plan.Stops.Skip(index).Select(x => x.Id).SequenceEqual(remaining.Select(x => x.Id))) return null;
    var leg = plan.Route.Legs[index];
    if (leg.Points.Count < 2 || !double.IsFinite(leg.Miles) || leg.Miles < 0) return null;
    var geometry = new RouteGeometry(new() { Legs = [leg] });
    var match = geometry.Match(position);
    // The result starts on the saved road; callers opting into a wider match must account for origin access separately.
    if (match.Away > maximumMatchMiles) return null;
    var atEndpoint = match.Point == leg.Points[^1] || Math.Abs(leg.Miles - match.Along) <= 1e-8;
    if (leg.Miles == 0 && leg.Points.Any(point => point != leg.Points[^1])) return null;
    if (atEndpoint)
    {
      // Keep the mandatory stop and its leg identity until actual tracking advances it.
      var endpoint = leg.Points[^1];
      return Remaining(new(0, 0, [endpoint, endpoint]));
    }
    if (match.Along >= leg.Miles) return null;
    var points = new List<RoutePoint> { match.Point };
    var lengths = leg.Points.Zip(leg.Points.Skip(1), RouteGeometry.Distance).ToArray();
    var total = lengths.Sum();
    if (total <= 0) return null;
    double along = 0;
    for (var i = 0; i < lengths.Length; i++)
    {
      along += lengths[i] / total * leg.Miles;
      if (along > match.Along) points.Add(leg.Points[i + 1]);
    }
    if (points.Count < 2) return null;
    var miles = leg.Miles - match.Along;
    return Remaining(new(miles, leg.Seconds * miles / leg.Miles, points));

    TruckRoute Remaining(RouteLeg first)
    {
      var legs = new List<RouteLeg> { first };
      legs.AddRange(plan.Route.Legs.Skip(index + 1));
      return new() { CalculatedAt = plan.Route.CalculatedAt, Legs = legs,
        Miles = legs.Sum(x => x.Miles), Seconds = legs.Sum(x => x.Seconds),
        Points = legs.SelectMany((x, i) => i == 0 ? x.Points : x.Points.Skip(1)).ToList(),
        Warnings = [.. plan.Route.Warnings] };
    }
  }
}
