// A port: something the rules need done for them that they cannot do
// themselves. Named here, with the rules that call it; answered outside.
using Domain.Models.Routing;

namespace Domain.Rules.Ports;

public interface IRouteSectionValidator
{
  void Validate(TruckRoute route, IReadOnlyList<RouteSection> sections);
}
