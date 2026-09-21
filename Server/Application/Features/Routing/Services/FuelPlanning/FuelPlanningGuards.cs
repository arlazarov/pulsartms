using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.FuelPlanning;

// What has to be true before fuel can be planned at all, each said as the
// sentence the dispatcher reads when it is not: there is a road, it was
// built for this truck as it is now, and the truck is known to be on it.
public static class FuelPlanningGuards
{
  public static RoutePlan RequirePlan(
    RoutePlanningState state,
    TruckRouteProfile profile
  )
  {
    if (profile.Validate(true) is { } error)
      throw new RoutePlanningException(error);
    return state.Plan
      ?? throw new RoutePlanningException("Build the truck route first.");
  }

  public static void RequireDrivable(
    RoutePlanningState state,
    RoutePlan plan,
    TruckRouteProfile profile
  )
  {
    if (plan.InputsChanged || !SameVehicle(profile, plan.Profile))
      throw new RoutePlanningException(
        "The truck or stops changed. Rebuild the route before planning fuel."
      );
    if (
      state.Progress?.RemainingMiles is null
      || state.Progress.ProgressMiles is null
      || state.Progress.Position?.IsValid != true
      || state.Progress.LocationStale
    )
      throw new RoutePlanningException(
        "Fuel quantities will update when a fresh GPS position is available on the current route."
      );
  }

  // A road is built for a vehicle's size and weight; money settings may
  // change without the road changing.
  public static bool SameVehicle(TruckRouteProfile a, TruckRouteProfile b) =>
    a.HeightFeet == b.HeightFeet
    && a.WidthFeet == b.WidthFeet
    && a.LengthFeet == b.LengthFeet
    && a.WeightPounds == b.WeightPounds
    && a.Axles == b.Axles
    && a.AxleWeightPounds == b.AxleWeightPounds
    && a.Hazmat == b.Hazmat;
}
