using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record PrepareTruckPlanningCommand(Guid TruckId)
  : IRequest<RequestResponse<AutomaticPlanningResult>>,
    IPlanningRequest;

public sealed class PrepareTruckPlanningValidator
  : AbstractValidator<PrepareTruckPlanningCommand>
{
  public PrepareTruckPlanningValidator()
  {
    RuleFor(x => x.TruckId).NotEmpty();
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
