using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record PrepareUpcomingPlanningCommand(
  Guid DispatchId,
  Guid? ExecutionLegId = null,
  Guid? TruckId = null
) : IRequest<RequestResponse<bool>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
  }
}

public sealed class PrepareUpcomingPlanningHandler(
  AutomaticPlanningService service
) : IRequestHandler<PrepareUpcomingPlanningCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<bool>> Handle(
    PrepareUpcomingPlanningCommand request,
    CancellationToken cancellationToken
  )
  {
    await service.PrepareUpcomingAsync(
      request.DispatchId,
      cancellationToken,
      request.ExecutionLegId,
      request.TruckId
    );
    return RequestResponse<bool>.Ok(true);
  }
}
