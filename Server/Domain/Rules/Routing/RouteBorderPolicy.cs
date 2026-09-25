using Domain.Models.Routing;
using Domain.Rules.Ports;

namespace Domain.Rules.Routing;

// A road between points that all lie in one country stays in that country.
// The provider's fastest truck road does not know this: from northern New
// York to Wisconsin it crosses Ontario (AMF1414, truck 11007, September 25),
// which a domestic load cannot do. So when every point asked for - the
// truck's position or the previous stop, each stop, and any waypoint a
// dispatcher chose - is in the same known country, the provider is asked to
// avoid border crossings, and the road it returns is checked, since the
// provider treats that as a preference. The check looks at every point of
// the road and at least every kilometre between them; a road it cannot
// fully place is Unknown, never Stays. A point in another country, or one
// whose country is not known, leaves the road to the provider as before:
// cross-border work and a dispatcher's own waypoint abroad keep their road.
public static class RouteBorderPolicy
{
  // Longest stretch between two looked-up points. A foreign excursion
  // shorter than this inside one straight segment of the geometry can go
  // unseen; road geometry bends far more often than that at a border.
  public const double StepKilometres = 1;

  // Most points one check looks up. AMF1414's road of 14,178 points needs
  // about 16,000; past this the answer is Unknown, never Stays, so bad
  // geometry (points a continent apart) costs a bounded 100,000 lookups.
  public const int MaximumLookups = 100_000;

  public static bool KeepsToOneCountry(IEnumerable<RouteRegion> regions) =>
    CountryOf(regions) is not null;

  // The one country every point is in, if there is one.
  public static string? CountryOf(IEnumerable<RouteRegion> regions)
  {
    var countries = regions.Select(region => region.Country).ToList();
    return
      countries.Count > 1
      && countries.All(country => country.Length > 0)
      && countries.Distinct(StringComparer.Ordinal).Count() == 1
      ? countries[0]
      : null;
  }

  // The same, judged by the road alone: its legs begin and end at the
  // points it was asked for, so a saved road carries its own stops.
  public static BorderVerdict Check(
    TruckRoute road,
    IRouteRegionLookup regions,
    CancellationToken ct
  ) =>
    road.Legs.Count == 0 || road.Legs.Any(leg => leg.Points.Count == 0)
      ? BorderVerdict.Unverified
      : Check(
        road,
        [road.Legs[0].Points[0], .. road.Legs.Select(leg => leg.Points[^1])],
        regions,
        ct
      );

  // Whether a road for one-country work stays in that country. Work that
  // is not in one known country is not judged. Every point of the road is
  // looked up, and more between points further apart than a step; the
  // first in another known country is where the road leaves. A point the
  // lookup cannot place, a coordinate that is not a place on Earth, a road
  // with no points or one needing more than MaximumLookups leaves the
  // answer Unknown.
  public static BorderVerdict Check(
    TruckRoute road,
    IReadOnlyList<RoutePoint> requested,
    IRouteRegionLookup regions,
    CancellationToken ct
  )
  {
    if (
      requested.Any(point => !point.IsValid)
      || CountryOf(requested.Select(regions.Find)) is not { } country
    )
      return BorderVerdict.NotJudged;
    var points = road.Legs.SelectMany(leg => leg.Points).ToList();
    if (points.Count == 0)
      points = [.. road.Points];
    if (points.Count == 0 || !points.All(point => point.IsValid))
      return BorderVerdict.Unverified;
    var unplaced = false;
    var lookups = 0;
    RoutePoint? previous = null;
    foreach (var point in points)
    {
      ct.ThrowIfCancellationRequested();
      var steps = Steps(previous, point);
      if (steps > MaximumLookups - lookups)
        return BorderVerdict.Unverified;
      lookups += steps;
      foreach (var at in Between(previous, point, steps))
      {
        var found = regions.Find(at).Country;
        if (found.Length == 0)
          unplaced = true;
        else if (found != country)
          return new(BorderCheck.Leaves, country, found, at);
      }
      previous = point;
    }
    return unplaced
      ? BorderVerdict.Unverified
      : new(BorderCheck.Stays, country, "", null);
  }

  // Lookups for the stretch ending at a point: the point itself, and more
  // so that none is more than a step from the next.
  private static int Steps(RoutePoint? from, RoutePoint to)
  {
    if (from is not { } start)
      return 1;
    var north = (to.Latitude - start.Latitude) * 111.2;
    var east =
      (to.Longitude - start.Longitude)
      * 111.2
      * Math.Cos(start.Latitude * Math.PI / 180);
    var kilometres = Math.Sqrt(north * north + east * east);
    return Math.Max(1, (int)Math.Ceiling(kilometres / StepKilometres));
  }

  // The point itself, preceded by the evenly spaced points before it.
  private static IEnumerable<RoutePoint> Between(
    RoutePoint? from,
    RoutePoint to,
    int steps
  )
  {
    if (from is { } start)
      for (var step = 1; step < steps; step++)
        yield return new(
          start.Latitude + (to.Latitude - start.Latitude) * step / steps,
          start.Longitude + (to.Longitude - start.Longitude) * step / steps
        );
    yield return to;
  }
}

public enum BorderCheck
{
  // The work is not in one known country, so no road is wrong for it.
  NotJudged,
  Stays,
  Leaves,

  // One-country work whose road could not be fully placed.
  Unknown,
}

// Country is the work's country; Entered and At say where a road that
// Leaves first reaches another one.
public sealed record BorderVerdict(
  BorderCheck Check,
  string Country,
  string Entered,
  RoutePoint? At
)
{
  public static BorderVerdict NotJudged { get; } =
    new(BorderCheck.NotJudged, "", "", null);
  public static BorderVerdict Unverified { get; } =
    new(BorderCheck.Unknown, "", "", null);

  public const string StaysValue = "stays";
  public const string UnknownValue = "unknown";
  public const string NotJudgedValue = "n/a";

  public bool Leaves => Check == BorderCheck.Leaves;

  // How a saved road records it: the country entered for a road that
  // leaves, otherwise one of the values above.
  public string Stored =>
    Check switch
    {
      BorderCheck.Leaves => Entered,
      BorderCheck.Stays => StaysValue,
      BorderCheck.Unknown => UnknownValue,
      _ => NotJudgedValue,
    };
}
