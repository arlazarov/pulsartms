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

  // Until disposed, one operation's checks of the same saved plan against
  // the same saved roads and history share one answer while no commit
  // this process knows of has touched the truck's inputs since.
  IDisposable Share() => Unshared.Instance;

  private sealed class Unshared : IDisposable
  {
    public static readonly Unshared Instance = new();

    public void Dispose() { }
  }
}
