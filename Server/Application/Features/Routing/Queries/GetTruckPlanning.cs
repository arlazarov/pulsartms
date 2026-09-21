using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetTruckPlanningQuery(
  Guid TruckId,
  Guid? KnownPlanId = null,
  int? KnownVersion = null
)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest,
    IChecked,
    IAboutWork
{
  public Guid? Truck => TruckId;

  public IEnumerable<string> Wrong()
  {
    if (TruckId == Guid.Empty)
      yield return "Choose a truck.";
  }
}

public sealed class GetTruckPlanningHandler(PlanningReadService service)
  : IRequestHandler<
    GetTruckPlanningQuery,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    GetTruckPlanningQuery request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.ForTruckAsync(
        request.TruckId,
        cancellationToken,
        request.KnownPlanId,
        request.KnownVersion
      )
    );
}
