using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record RecalculateFuelPlanCommand(
  Guid DispatchId,
  Guid? ExecutionLegId = null,
  long? AssignmentRevision = null
)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest,
    IChecked
{
  internal DateTime? AutomaticRefreshRevision { get; init; }

  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (ExecutionLegId == Guid.Empty)
      yield return "Reopen the load before recalculating its fuel.";
    if (ExecutionLegId.HasValue && AssignmentRevision is not > 0)
      yield return "Reopen the load before recalculating its fuel.";
  }
}

public sealed class RecalculateFuelPlanHandler(AutomaticPlanningService service)
  : IRequestHandler<
    RecalculateFuelPlanCommand,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    RecalculateFuelPlanCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.RecalculateFuelAsync(
        request.DispatchId,
        cancellationToken,
        request.ExecutionLegId,
        request.AssignmentRevision,
        request.AutomaticRefreshRevision
      )
    );
}
