using Domain.Rules.Ports;

namespace Domain.Rules.Routing;

// A road between points that all lie in one country stays in that country.
// The provider's fastest truck road does not know this: from northern New
// York to Wisconsin it crosses Ontario (AMF1414, truck 11007, September 25),
// which a domestic load cannot do. So when every point asked for - the
// truck's position or the previous stop, each stop, and any waypoint a
// dispatcher chose - is in the same known country, the provider is asked to
// avoid border crossings. A point in another country, or one whose country
// is not known, leaves the road to the provider as before: cross-border work
// and a dispatcher's own waypoint abroad keep their road.
public static class RouteBorderPolicy
{
  // Part of every saved road's signature, so roads bought before the rule
  // are bought again once, through the usual refresh.
  public const string Version = "one-country-v1";

  public static bool KeepsToOneCountry(IEnumerable<RouteRegion> regions)
  {
    var countries = regions.Select(region => region.Country).ToList();
    return countries.Count > 1
      && countries.All(country => country.Length > 0)
      && countries.Distinct(StringComparer.Ordinal).Count() == 1;
  }
}
