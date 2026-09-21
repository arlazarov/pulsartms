using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

// How much fuel the truck starts the plan with: what the dispatcher typed,
// or what the tank gauge says - and a gauge reading is only believed if it
// is a percentage, and not from the future.
public static class FuelStartingLevel
{
  public static double Gallons(
    double? entered,
    RoutePlanningState state,
    TruckRouteProfile profile,
    DateTime now
  )
  {
    double gallons;
    if (entered.HasValue)
      gallons = entered.Value;
    else
    {
      if (
        state.FuelPercent is not { } percent
        || !double.IsFinite(percent)
        || percent is < 0 or > 100
        || state.FuelUpdatedAt is null
        || state.FuelUpdatedAt > now.AddMinutes(1)
      )
        throw new RoutePlanningException(
          "No valid fuel level is available for this truck."
        );
      gallons = profile.TankGallons!.Value * percent / 100;
    }
    if (
      FuelReservePolicy.StartingLevelError(gallons, profile) is { } levelError
    )
      throw new RoutePlanningException(levelError);
    return gallons;
  }
}
