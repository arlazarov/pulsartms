using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record BuildRouteCommand(Guid DispatchId, RouteBuildRequest Route)
  : IRequest<RequestResponse<RoutePlan>>,
    IPlanningRequest;

public sealed class BuildRouteValidator : AbstractValidator<BuildRouteCommand>
{
  public BuildRouteValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Route).NotNull();
    When(
      x => x.Route is not null,
      () => RuleFor(x => x.Route.Profile).NotNull()
    );
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
