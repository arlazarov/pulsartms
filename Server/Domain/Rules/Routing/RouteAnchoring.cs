using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class RouteAnchoring
{
  public const double FacilityToleranceMiles = .5;
  public const double ContinuityToleranceMiles = .05;

  public static bool Matches(
    TruckRoute? route,
    IReadOnlyList<RoutePoint> stops
  ) =>
    stops.Count >= 2
    && route?.Legs is { } legs
    && legs.Count == stops.Count - 1
    && legs.Select(
        (leg, index) =>
          LegMatches(leg, stops[index], stops[index + 1])
          && (
            index == 0
            || Near(
              legs[index - 1].Points[^1],
              leg.Points[0],
              ContinuityToleranceMiles
            )
          )
      )
      .All(x => x);

  public static bool LegMatches(
    RouteLeg? leg,
    RoutePoint from,
    RoutePoint to
  ) =>
    leg?.Points is { Count: >= 2 } points
    && Near(points[0], from, FacilityToleranceMiles)
    && Near(points[^1], to, FacilityToleranceMiles);

  public static bool Continuous(TruckRoute? first, TruckRoute? next) =>
    first?.Legs is { Count: > 0 } before
    && next?.Legs is { Count: > 0 } after
    && before[^1].Points is { Count: >= 2 } end
    && after[0].Points is { Count: >= 2 } start
    && Near(end[^1], start[0], ContinuityToleranceMiles);

  public static bool Near(
    RoutePoint? first,
    RoutePoint? second,
    double toleranceMiles
  )
  {
    if (first?.IsValid != true || second?.IsValid != true)
      return false;
    var latitudeMiles = (first.Latitude - second.Latitude) * 69;
    var longitudeMiles =
      (first.Longitude - second.Longitude)
      * 69
      * Math.Cos((first.Latitude + second.Latitude) * Math.PI / 360);
    return latitudeMiles * latitudeMiles + longitudeMiles * longitudeMiles
      <= toleranceMiles * toleranceMiles;
  }
}
