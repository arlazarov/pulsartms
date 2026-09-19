using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Queries;

public sealed record GetRoutePreviewsQuery()
  : IRequest<RequestResponse<List<AutomaticPlanningResult>>>,
    IPlanningRequest;

public sealed class GetRoutePreviewsHandler(RoutePreviewService service)
  : IRequestHandler<
    GetRoutePreviewsQuery,
    RequestResponse<List<AutomaticPlanningResult>>
  >
{
  public async Task<RequestResponse<List<AutomaticPlanningResult>>> Handle(
    GetRoutePreviewsQuery request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<List<AutomaticPlanningResult>>.Ok(
      await service.GetAsync(cancellationToken)
    );
}
