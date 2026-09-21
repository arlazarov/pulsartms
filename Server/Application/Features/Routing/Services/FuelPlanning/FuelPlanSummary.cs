using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// What a chosen fuel plan says about itself: the warning on a first stop
// reached below reserve, which chains it beat, which of its prices are
// guesses, and what its distances are estimates of. Written for the
// dispatcher, in the order a dispatcher asks.
public static class FuelPlanSummary
{
  public static void Write(
    FuelPlan fuel,
    TruckRouteProfile profile,
    List<FuelRouteCheck> checks,
    FuelRouteCheck winner,
    FuelPriceCalendar calendar,
    FuelHorizonResult horizon,
    TruckRoute baseline,
    int evaluatedRoutes
  )
  {
    if (
      fuel.Stops.FirstOrDefault() is { } firstPurchase
      && firstPurchase.ArrivalGallons < profile.ReserveGallons
    )
    {
      firstPurchase.Warning = FuelReservePolicy.ArrivalWarning(
        firstPurchase.ArrivalGallons,
        profile
      );
      fuel.Notes.Add(firstPurchase.Warning);
    }
    winner.Result = "Selected";
    fuel.RouteChecks = checks;
    fuel.UsDiscountSignature = calendar.Signature;
    fuel.PriceDates = calendar.Dates;
    if (fuel.Stops.Any(x => x.PriceEstimated))
      fuel.Notes.Add(
        "Prices without a published arrival-date quote are estimated using today's available price; unknown arrival times also use today's price."
      );
    fuel.EstimatedStationAccess = true;
    fuel.StartAccessMiles = horizon.StartAccessMiles;
    fuel.RemainingMiles =
      baseline.Miles
      + horizon.StartAccessMiles
      + fuel.Stops.Sum(x => x.DetourMiles);
    fuel.Notes.Add(
      $"Compared {evaluatedRoutes} fuel chains on the saved route without routing requests. Station access distance and time are estimates, not verified truck approaches."
    );
    fuel.Notes.AddRange(horizon.Notes);
    fuel.Notes.Add(
      "Additional fuel stops must save at least $20 each against a feasible alternative with fewer stops and the same schedule rank. This is a selection threshold, not a stop charge."
    );
  }
}
