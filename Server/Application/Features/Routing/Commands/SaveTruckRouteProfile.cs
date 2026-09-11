using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record SaveTruckRouteProfileCommand(Guid DispatchId, TruckRouteProfile Profile) : IRequest<RequestResponse<TruckRouteProfile>>, IPlanningRequest;

public sealed class SaveTruckRouteProfileValidator : AbstractValidator<SaveTruckRouteProfileCommand>
{
  public SaveTruckRouteProfileValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Profile).NotNull();
  }
}

public sealed class SaveTruckRouteProfileHandler(RoutePlanningService service)
  : IRequestHandler<SaveTruckRouteProfileCommand, RequestResponse<TruckRouteProfile>>
{
  public async Task<RequestResponse<TruckRouteProfile>> Handle(SaveTruckRouteProfileCommand request, CancellationToken cancellationToken)
    => RequestResponse<TruckRouteProfile>.Ok(await service.SaveProfileAsync(request.DispatchId, request.Profile, cancellationToken));
}
