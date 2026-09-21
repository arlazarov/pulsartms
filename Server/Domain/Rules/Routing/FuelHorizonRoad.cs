using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

// Joining the saved roads of several loads into the one road a fuel plan is
// calculated along, and refusing to when they do not meet or when together
// they are more geometry than a search should be given.
public static class FuelHorizonRoad
{
  private const int MaximumGeometryPoints = 200_000;

  public static TruckRoute RemainingExtension(
    TruckRoute route,
    IReadOnlyList<RouteWorkStop> ordered,
    IReadOnlyList<PlanStop> remaining
  )
  {
    var selected = remaining.Select(stop => stop.Id).ToHashSet();
    var legs = new List<RouteLeg>();
    var start = 0;
    for (var i = 0; i < ordered.Count; i++)
    {
      if (!selected.Contains(ordered[i].Id))
        continue;
      var part = route.Legs.Skip(start).Take(i - start + 1).ToArray();
      // Keep the saved path through completed intermediate stops without
      // inventing a shortcut.
      legs.Add(
        part.Length == 1
          ? part[0]
          : new(
            part.Sum(leg => leg.Miles),
            part.Sum(leg => leg.Seconds),
            part.SelectMany(
                (leg, index) => index == 0 ? leg.Points : leg.Points.Skip(1)
              )
              .ToList()
          )
      );
      start = i + 1;
    }
    return new()
    {
      CalculatedAt = route.CalculatedAt,
      Legs = legs,
      Miles = legs.Sum(leg => leg.Miles),
      Seconds = legs.Sum(leg => leg.Seconds),
      Warnings = [.. route.Warnings],
    };
  }

  public static TruckRoute Join(TruckRoute first, TruckRoute next)
  {
    if (
      !first.TryGetLegSeconds(out var firstSeconds)
      || !next.TryGetLegSeconds(out var nextSeconds)
    )
      throw new RoutePlanningException(
        "A saved route has inconsistent timing. Rebuild the route before finding fuel."
      );
    if (!RouteAnchoring.Continuous(first, next))
      throw new RoutePlanningException(
        "The saved route segments do not connect. Rebuild the affected connection before finding fuel."
      );
    RequirePointBudget(
      first.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0))
        + next.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0))
    );
    return new()
    {
      CalculatedAt =
        first.CalculatedAt < next.CalculatedAt
          ? first.CalculatedAt
          : next.CalculatedAt,
      Miles = first.Miles + next.Miles,
      Seconds = firstSeconds + nextSeconds,
      Legs = [.. first.Legs, .. next.Legs],
      Warnings = [.. first.Warnings, .. next.Warnings],
    };
  }

  public static void RequireAnchored(
    TruckRoute route,
    IReadOnlyList<RoutePoint> points
  )
  {
    if (
      !SavedRouteGeometry.Complete(route, points.Count - 1)
      || !RouteAnchoring.Matches(route, points)
    )
      throw new RoutePlanningException(
        "The fuel route does not reach the confirmed stops. The saved fuel plan has been kept."
      );
    RequirePointBudget(route.Legs.Sum(leg => (long)leg.Points.Count));
  }

  public static void RequirePointBudget(long points)
  {
    if (points > MaximumGeometryPoints)
      throw new RoutePlanningException(
        "The complete assigned route exceeds the supported geometry limit. The saved fuel plan has been kept."
      );
  }
}
