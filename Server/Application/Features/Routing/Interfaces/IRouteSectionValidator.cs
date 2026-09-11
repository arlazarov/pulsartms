using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface IRouteSectionValidator
{
  void Validate(TruckRoute route, IReadOnlyList<RouteSection> sections);
}
