using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record PrepareDispatchPlanningCommand(Guid DispatchId) : IRequest<RequestResponse<AutomaticPlanningResult>>, IPlanningRequest;

public sealed class PrepareDispatchPlanningValidator : AbstractValidator<PrepareDispatchPlanningCommand>
{
  public PrepareDispatchPlanningValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
  }
}

public sealed class PrepareDispatchPlanningHandler(AutomaticPlanningService service)
  : IRequestHandler<PrepareDispatchPlanningCommand, RequestResponse<AutomaticPlanningResult>>
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(PrepareDispatchPlanningCommand request, CancellationToken cancellationToken)
    => RequestResponse<AutomaticPlanningResult>.Ok(await service.ForDispatchAsync(request.DispatchId, cancellationToken));
}
