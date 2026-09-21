using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetDispatchPlanningQuery(
  Guid DispatchId,
  Guid? KnownPlanId = null,
  int? KnownVersion = null
)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
  }
}

public sealed class GetDispatchPlanningHandler(PlanningReadService service)
  : IRequestHandler<
    GetDispatchPlanningQuery,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    GetDispatchPlanningQuery request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.ForDispatchAsync(
        request.DispatchId,
        cancellationToken,
        request.KnownPlanId,
        request.KnownVersion
      )
    );
}
