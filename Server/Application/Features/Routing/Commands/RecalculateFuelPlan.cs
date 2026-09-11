using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record RecalculateFuelPlanCommand(Guid DispatchId) : IRequest<RequestResponse<AutomaticPlanningResult>>, IPlanningRequest;

public sealed class RecalculateFuelPlanValidator : AbstractValidator<RecalculateFuelPlanCommand>
{
  public RecalculateFuelPlanValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
  }
}

public sealed class RecalculateFuelPlanHandler(AutomaticPlanningService service)
  : IRequestHandler<RecalculateFuelPlanCommand, RequestResponse<AutomaticPlanningResult>>
{
  public async Task<RequestResponse<AutomaticPlanningResult>> Handle(RecalculateFuelPlanCommand request, CancellationToken cancellationToken)
    => RequestResponse<AutomaticPlanningResult>.Ok(await service.RecalculateFuelAsync(request.DispatchId, cancellationToken));
}
