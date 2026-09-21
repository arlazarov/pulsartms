using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record BuildRouteCommand(Guid DispatchId, RouteBuildRequest Route)
  : IRequest<RequestResponse<RoutePlan>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Route is null)
      yield return "The route request is missing.";
    else if (Route.Profile is null)
      yield return "The truck profile is missing.";
  }
}

public sealed class BuildRouteHandler(RoutePlanningService service)
  : IRequestHandler<BuildRouteCommand, RequestResponse<RoutePlan>>
{
  public async Task<RequestResponse<RoutePlan>> Handle(
    BuildRouteCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<RoutePlan>.Ok(
      await service.BuildAsync(
        request.DispatchId,
        request.Route,
        cancellationToken
      )
    );
}
