using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Queries;

public sealed record GetTruckRoutePreviewQuery(Guid TruckId)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest;

public sealed class GetTruckRoutePreviewValidator
  : AbstractValidator<GetTruckRoutePreviewQuery>
{
  public GetTruckRoutePreviewValidator() => RuleFor(x => x.TruckId).NotEmpty();
}

public sealed class GetTruckRoutePreviewHandler(RoutePreviewService service)
  : IRequestHandler<
    GetTruckRoutePreviewQuery,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    GetTruckRoutePreviewQuery request,
    CancellationToken ct
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.ForTruckAsync(request.TruckId, ct)
    );
}
