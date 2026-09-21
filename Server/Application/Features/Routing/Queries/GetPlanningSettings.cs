using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetPlanningSettingsQuery()
  : IRequest<RequestResponse<PlanningSettingsState>>,
    IPlanningRequest;

public sealed class GetPlanningSettingsHandler(PlanningSettingsService service)
  : IRequestHandler<
    GetPlanningSettingsQuery,
    RequestResponse<PlanningSettingsState>
  >
{
  public async Task<RequestResponse<PlanningSettingsState>> Handle(
    GetPlanningSettingsQuery request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<PlanningSettingsState>.Ok(
      await service.GetAsync(cancellationToken)
    );
}
