using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IFuelSavedInputsValidation
{
  Task<bool> MatchesAsync(FuelRecommendations access, CancellationToken ct);

  Task<bool> MatchesAsync(
    TruckFuelPlanSnapshot saved,
    RoutePlan current,
    CancellationToken ct
  );
}
