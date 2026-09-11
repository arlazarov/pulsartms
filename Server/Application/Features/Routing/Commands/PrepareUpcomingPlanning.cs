using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record PrepareUpcomingPlanningCommand(Guid DispatchId) : IRequest<RequestResponse<bool>>, IPlanningRequest;

public sealed class PrepareUpcomingPlanningValidator : AbstractValidator<PrepareUpcomingPlanningCommand>
{
  public PrepareUpcomingPlanningValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
  }
}

public sealed class PrepareUpcomingPlanningHandler(AutomaticPlanningService service)
  : IRequestHandler<PrepareUpcomingPlanningCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<bool>> Handle(PrepareUpcomingPlanningCommand request, CancellationToken cancellationToken)
  {
    await service.PrepareUpcomingAsync(request.DispatchId, cancellationToken);
    return RequestResponse<bool>.Ok(true);
  }
}
