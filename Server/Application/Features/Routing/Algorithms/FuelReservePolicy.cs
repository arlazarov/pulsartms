using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class FuelReservePolicy
{
  public static string? StartingLevelError(double gallons, TruckRouteProfile profile)
  {
    if (!double.IsFinite(gallons) || gallons < 0 || gallons > profile.TankGallons)
      return "Confirm the current fuel quantity; it must be within the truck's tank capacity.";
    return gallons == 0
      ? "The reported tank is empty. Confirm the fuel level or arrange refueling before driving."
      : null;
  }

  // Only the initial trip to a purchase may use reserve that is already depleted.
  public static double FirstArrivalMinimum(double startingGallons, TruckRouteProfile profile) =>
    startingGallons < profile.ReserveGallons ? 0 : Math.Ceiling(profile.ReserveGallons);

  public static bool PurchaseNotPassed(double milesAhead, double progressMiles) =>
    milesAhead >= progressMiles - .05;
}
