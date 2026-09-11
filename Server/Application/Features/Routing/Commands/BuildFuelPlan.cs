using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

public sealed record BuildFuelPlanCommand(Guid DispatchId, FuelBuildRequest Fuel) : IRequest<RequestResponse<FuelPlan>>, IPlanningRequest;

public sealed class BuildFuelPlanValidator : AbstractValidator<BuildFuelPlanCommand>
{
  public BuildFuelPlanValidator()
  {
    RuleFor(x => x.DispatchId).NotEmpty();
    RuleFor(x => x.Fuel).NotNull();
    When(x => x.Fuel is not null, () => RuleFor(x => x.Fuel.Profile).NotNull());
  }
}

public sealed class BuildFuelPlanHandler(FuelPlanningService service)
  : IRequestHandler<BuildFuelPlanCommand, RequestResponse<FuelPlan>>
{
  public async Task<RequestResponse<FuelPlan>> Handle(BuildFuelPlanCommand request, CancellationToken cancellationToken)
    => RequestResponse<FuelPlan>.Ok(await service.BuildAsync(request.DispatchId, request.Fuel, cancellationToken));
}
