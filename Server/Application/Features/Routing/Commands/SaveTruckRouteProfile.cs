using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record SaveTruckRouteProfileCommand(
  Guid DispatchId,
  TruckRouteProfile Profile
) : IRequest<RequestResponse<TruckRouteProfile>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Profile is null)
      yield return "The truck profile is missing.";
  }
}

public sealed class SaveTruckRouteProfileHandler(RoutePlanningService service)
  : IRequestHandler<
    SaveTruckRouteProfileCommand,
    RequestResponse<TruckRouteProfile>
  >
{
  public async Task<RequestResponse<TruckRouteProfile>> Handle(
    SaveTruckRouteProfileCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<TruckRouteProfile>.Ok(
      await service.SaveProfileAsync(
        request.DispatchId,
        request.Profile,
        cancellationToken
      )
    );
}
