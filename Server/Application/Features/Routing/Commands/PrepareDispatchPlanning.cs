using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record PrepareDispatchPlanningCommand(
  Guid DispatchId,
  Guid? ExecutionLegId = null,
  long? AssignmentRevision = null
) : IRequest<RequestResponse<AutomaticPlanningResult>>, IPlanningRequest;

public sealed class PrepareDispatchPlanningValidator
  : AbstractValidator<PrepareDispatchPlanningCommand>
{
  public PrepareDispatchPlanningValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
  }
}

public sealed class PrepareDispatchPlanningHandler(
  AutomaticPlanningService service
)
  : IRequestHandler<
    PrepareDispatchPlanningCommand,
    RequestResponse<AutomaticPlanningResult>
  >
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(
    PrepareDispatchPlanningCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<AutomaticPlanningResult>.Ok(
      await service.ForDispatchAsync(
        request.DispatchId,
        cancellationToken,
        executionLegId: request.ExecutionLegId,
        assignmentRevision: request.AssignmentRevision
      )
    );
}
