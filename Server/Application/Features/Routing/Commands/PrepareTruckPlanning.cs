using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record PrepareTruckPlanningCommand(Guid TruckId)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (TruckId == Guid.Empty)
      yield return "Choose a truck.";
  }
}

public sealed class PrepareTruckPlanningHandler(
  AutomaticPlanningService service
)
  : IRequestHandler<
    PrepareTruckPlanningCommand,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    PrepareTruckPlanningCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.ForTruckAsync(request.TruckId, cancellationToken)
    );
}
