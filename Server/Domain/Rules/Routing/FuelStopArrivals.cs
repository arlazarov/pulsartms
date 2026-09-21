using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static class FuelStopArrivals
{
  public static List<FuelStopArrival> Calculate(
    FuelPlan fuel,
    IReadOnlyList<FuelItineraryStop> stops,
    TruckRouteProfile profile,
    double progressMiles = 0
  )
  {
    if (
      fuel.NeedsRefresh
      || profile.Mpg is not > 0
      || profile.TankGallons is not > 0
      || !double.IsFinite(profile.Mpg.Value)
      || !double.IsFinite(profile.TankGallons.Value)
      || !double.IsFinite(fuel.StartingGallons)
      || !double.IsFinite(progressMiles)
      || !double.IsFinite(fuel.StartAccessMiles)
      || fuel.StartAccessMiles < 0
    )
      return [];
    var remaining = fuel.Stops.ToList();
    var result = new List<FuelStopArrival>();
    double purchased = 0,
      access = fuel.StartAccessMiles,
      previous = progressMiles;
    foreach (var visit in stops)
    {
      if (!double.IsFinite(visit.EndMiles) || visit.EndMiles < previous)
        return [];
      previous = visit.EndMiles;
      // Mandatory-leg ownership disambiguates repeated locations and purchases
      // at stop endpoints.
      foreach (
        var purchase in remaining
          .Where(x =>
            x.DispatchId == visit.DispatchId && x.BeforeStopId == visit.Stop.Id
          )
          .ToArray()
      )
      {
        if (
          !double.IsFinite(purchase.BuyGallons)
          || purchase.BuyGallons < 0
          || !double.IsFinite(purchase.DetourMiles)
          || purchase.DetourMiles < 0
        )
          return [];
        purchased += purchase.BuyGallons;
        if (fuel.EstimatedStationAccess)
          access += purchase.DetourMiles;
        remaining.Remove(purchase);
      }
      var gallons =
        fuel.StartingGallons
        + purchased
        - (visit.EndMiles - progressMiles + access) / profile.Mpg.Value;
      if (
        !double.IsFinite(gallons)
        || gallons < -1e-9
        || gallons > profile.TankGallons.Value + 1e-9
      )
        return [];
      gallons = Math.Clamp(gallons, 0, profile.TankGallons.Value);
      result.Add(
        new(
          visit.DispatchId,
          visit.Stop.Id,
          gallons,
          gallons / profile.TankGallons.Value * 100
        )
      );
    }
    return remaining.Count == 0 ? result : [];
  }
}
