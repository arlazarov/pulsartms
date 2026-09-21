using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Routing.Commands;

public sealed record BuildFuelPlanCommand(
  Guid DispatchId,
  FuelBuildRequest Fuel
) : IRequest<RequestResponse<FuelCalculationResult>>, IPlanningRequest, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Fuel is null)
      yield return "The fuel request is missing.";
    else if (Fuel.Profile is null)
      yield return "The truck profile is missing.";
  }
}

public sealed class BuildFuelPlanHandler(FuelPlanningService service)
  : IRequestHandler<
    BuildFuelPlanCommand,
    RequestResponse<FuelCalculationResult>
  >
{
  public async Task<RequestResponse<FuelCalculationResult>> Handle(
    BuildFuelPlanCommand request,
    CancellationToken cancellationToken
  ) =>
    RequestResponse<FuelCalculationResult>.Ok(
      await service.BuildAsync(
        request.DispatchId,
        request.Fuel,
        cancellationToken
      )
    );
}
