using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Queries;

public sealed record GetRoutePlanningQuery(Guid DispatchId) : IRequest<RequestResponse<RoutePlanningState>>, IPlanningRequest;

public sealed class GetRoutePlanningValidator : AbstractValidator<GetRoutePlanningQuery>
{
  public GetRoutePlanningValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
  }
}

public sealed class GetRoutePlanningHandler(RoutePlanningService service)
  : IRequestHandler<GetRoutePlanningQuery, RequestResponse<RoutePlanningState>>
{
  public async Task<RequestResponse<RoutePlanningState>> Handle(GetRoutePlanningQuery request, CancellationToken cancellationToken)
    => RequestResponse<RoutePlanningState>.Ok(await service.GetAsync(request.DispatchId, cancellationToken));
}
