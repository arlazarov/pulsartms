// A port: something the rules need done for them that they cannot do
// themselves. Named here, with the rules that call it; answered outside.
using Domain.Models.Routing;

namespace Domain.Rules.Ports;

public record RouteRegion(string Country, string TimeZoneId, bool NorthOf60);

public interface IRouteRegionLookup
{
  RouteRegion Find(RoutePoint point);
}
