using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class FuelReservePolicy
{
  public static string? StartingLevelError(
    double gallons,
    TruckRouteProfile profile
  )
  {
    if (
      !double.IsFinite(gallons)
      || gallons < 0
      || gallons > profile.TankGallons
    )
      return "Confirm the current fuel quantity; it must be within the truck's tank capacity.";
    return null;
  }

  public const double PhysicalArrivalMinimumGallons = 0;

  public static string ArrivalWarning(double gallons, TruckRouteProfile profile)
  {
    if (gallons >= profile.ReserveGallons)
      return "";
    return gallons < 0
      ? FormattableString.Invariant(
        $"Cannot reach this station: {-gallons:N1} US gal short. Refuel before driving to it."
      )
      : FormattableString.Invariant(
        $"Below reserve: estimated arrival {gallons:N1} US gal; "
      )
        + FormattableString.Invariant(
          $"{profile.ReserveGallons - gallons:N1} US gal below the configured reserve."
        );
  }

  public static bool PurchaseNotPassed(
    double milesAhead,
    double progressMiles
  ) => milesAhead >= progressMiles - .05;
}
