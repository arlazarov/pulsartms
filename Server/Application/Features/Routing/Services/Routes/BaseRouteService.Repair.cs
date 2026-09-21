using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

// Putting a road back when the saved one cannot be used: it is bought
// again and refused unless it reaches every confirmed stop, because a
// road that misses a stop is worse than no road at all.
public sealed partial class BaseRouteService
{
  private sealed record BaseRoadVersion(
    Guid Id,
    string InputHash,
    DateTime CalculatedAt
  );

  private async Task<TruckRoute> RepairAsync(
    TruckRoute? saved,
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (saved is null)
    {
      var fresh = await routing.CalculateAsync(points, profile, ct);
      RequireAnchored(fresh, points);
      return fresh;
    }
    var legs = saved.Legs.ToList();
    var invalid = legs.Select(
        (leg, index) =>
          !RouteAnchoring.LegMatches(leg, points[index], points[index + 1])
      )
      .ToArray();
    for (var index = 1; index < legs.Count; index++)
      if (
        !invalid[index - 1]
        && !invalid[index]
        && !RouteAnchoring.Near(
          legs[index - 1].Points[^1],
          legs[index].Points[0],
          RouteAnchoring.ContinuityToleranceMiles
        )
      )
        invalid[index] = true;
    var warnings = saved.Warnings.ToList();
    var calculatedAt = saved.CalculatedAt;
    for (var first = 0; first < invalid.Length; first++)
    {
      if (!invalid[first])
        continue;
      var last = first;
      while (last + 1 < invalid.Length && invalid[last + 1])
        last++;
      var requested = points.Skip(first).Take(last - first + 2).ToList();
      if (first > 0)
        requested[0] = legs[first - 1].Points[^1];
      if (last + 1 < legs.Count)
        requested[^1] = legs[last + 1].Points[0];
      var repaired = await routing.CalculateAsync(requested, profile, ct);
      RequireAnchored(repaired, requested);
      for (var index = first; index <= last; index++)
        legs[index] = repaired.Legs[index - first];
      warnings.AddRange(repaired.Warnings);
      if (repaired.CalculatedAt < calculatedAt)
        calculatedAt = repaired.CalculatedAt;
      first = last;
    }
    var result = new TruckRoute
    {
      Legs = legs,
      Miles = legs.Sum(leg => leg.Miles),
      Seconds = legs.Sum(leg => leg.Seconds),
      Points = legs.SelectMany(
          (leg, index) => index == 0 ? leg.Points : leg.Points.Skip(1)
        )
        .ToList(),
      Warnings = warnings.Distinct().ToList(),
      CalculatedAt = calculatedAt,
    };
    RequireAnchored(result, points);
    return result;
  }

  private static void RequireAnchored(
    TruckRoute route,
    IReadOnlyList<RoutePoint> points
  )
  {
    if (!SavedRouteGeometry.Complete(route, points.Count - 1))
      throw new RoutePlanningException(
        "The base route geometry is incomplete."
      );
    if (!RouteAnchoring.Matches(route, points))
      throw new RoutePlanningException(
        "The base route does not reach the confirmed stops. The saved route has been kept."
      );
  }
}
