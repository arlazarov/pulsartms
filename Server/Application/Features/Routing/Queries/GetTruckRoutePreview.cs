using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Queries;

public sealed record GetTruckRoutePreviewQuery(Guid TruckId)
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
