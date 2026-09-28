namespace Domain.Rules.Routing;

// The terms a fuel plan's cost is made of (audit F25): one owner for the
// arithmetic of each. The optimizer, the chain comparison, the manual
// replay, the projection and the price refresh each still choose which
// terms they add and from which inputs - a clamp, where access minutes
// come from, whether the initial access is charged. Those choices are
// visible at each caller and differ today; changing them changes figures
// dispatchers see and is decided separately.
public static class FuelPlanCost
{
  // What gallons cost at a price per gallon; also the change in cost when
  // the price per gallon changes by that much.
  public static double Purchase(double gallons, double pricePerGallon) =>
    gallons * pricePerGallon;

  // Driver time, priced by the hour.
  public static double AccessTime(double minutes, double driverHourlyUsd) =>
    minutes / 60 * driverHourlyUsd;

  // The fuel the plan leaves short of its arrival target, valued at the
  // replacement price; nothing when it arrives at or above the target.
  public static double FutureFuel(
    double targetGallons,
    double arrivingGallons,
    double replacementPriceUsd
  ) => Math.Max(0, targetGallons - arrivingGallons) * replacementPriceUsd;

  // One stop's economic cost: its purchase, the stop charge and its access
  // time, in that order.
  public static double Stop(
    double gallons,
    double economicPricePerGallon,
    double stopCostUsd,
    double accessMinutes,
    double driverHourlyUsd
  ) =>
    Purchase(gallons, economicPricePerGallon)
    + stopCostUsd
    + AccessTime(accessMinutes, driverHourlyUsd);
}
