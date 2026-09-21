using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetRoutePlanningQuery(Guid DispatchId)
  : IRequest<RequestResponse<RoutePlanningState>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
  }
}

public sealed class GetRoutePlanningHandler(RoutePlanningService service)
  : IRequestHandler<GetRoutePlanningQuery, RequestResponse<RoutePlanningState>>
{
  public async Task<RequestResponse<RoutePlanningState>> Handle(
    GetRoutePlanningQuery request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<RoutePlanningState>.Ok(
      await service.GetAsync(request.DispatchId, cancellationToken)
    );
}
