using Domain.Models.Routing;
using Domain.Rules.Ports;

namespace Domain.Rules.Routing;

public sealed class RouteSectionValidator : IRouteSectionValidator
{
  public void Validate(TruckRoute route, IReadOnlyList<RouteSection> sections)
  {
    if (sections.Any(s => s.Restricted))
      Add(
        "TomTom reports truck restrictions. Check access before driving this route."
      );
    if (sections.Any(s => s.Unsupported))
      Add("TomTom has not confirmed truck access on some route sections.");

    void Add(string warning)
    {
      if (!route.Warnings.Contains(warning))
        route.Warnings.Add(warning);
    }
  }
}
