using System.Text.Json;
using Domain.Models.Fleet;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Whether a saved fuel plan can still be trusted, and if not, why - said in
// the words the dispatcher reads.
//
// The reasons are not equal. One that invalidates the plan hides it; one
// that only ages its prices, or leaves the truck's position unknown, keeps
// it readable. All of them still ask for a recalculation.
public static class FuelPlanFreshness
{
  public static void Judge(
    RoutePlan plan,
    FuelPlan fuel,
    TruckRouteProfile profile,
    RouteProgress? progress,
    TruckLocation? truck,
    string policySignature,
    DateTime now
  )
  {
    var changed =
      fuel.SelectionVersion != FuelOptimizer.SelectionVersion
      || fuel.ArrivalPolicy?.PolicySignature != policySignature
      || fuel.ProfileSignature
        != JsonSerializer.Serialize(profile, RoutingJson.Options)
      || plan.InputsChanged
      || fuel.RouteVersion != plan.Version;
    fuel.RefreshReasons = [];
    var invalid = false;
    var priced = false;
    var unverified = false;
    if (changed || fuel.DispatchIds.Count == 0)
    {
      fuel.RefreshReasons.Add("Route or fuel settings changed.");
      invalid = true;
    }
    if (progress?.OffRoute == true)
    {
      fuel.RefreshReasons.Add("Truck is off the calculated route.");
      invalid = true;
    }
    if (progress?.LocationStale == true)
    {
      // An old GPS fix says how far along he is is unknown. It does not say
      // where he has to fuel: the stations, volumes and prices are
      // unchanged. Hiding the plan leaves the driver with no fuel stop at
      // all, which is the worse answer.
      fuel.RefreshReasons.Add("Fresh GPS is needed to verify the plan.");
      unverified = true;
    }
    if (now - fuel.CalculatedAt > TimeSpan.FromMinutes(30))
    {
      fuel.RefreshReasons.Add("Check current fuel prices and quantities.");
      priced = true;
    }
    if (
      progress?.ProgressMiles is { } along
      && truck?.FuelPercent is { } level
      && profile.Mpg is > 0
      && profile.TankGallons is > 0
    )
    {
      var used =
        Math.Max(0, along - fuel.StartProgressMiles) / profile.Mpg!.Value;
      var expected = fuel.StartingGallons - used;
      var actual = (double)level * profile.TankGallons!.Value / 100;
      if (
        Math.Abs(actual - expected)
        > Math.Max(10, profile.TankGallons.Value * .08)
      )
      {
        fuel.RefreshReasons.Add(
          "Fuel level differs from the plan. Recalculate from the latest reading."
        );
        invalid = true;
      }
    }
    fuel.NeedsRefresh = fuel.RefreshReasons.Count > 0;
    fuel.PricesOutOfDate = !invalid && (priced || fuel.PricesOutOfDate);
    fuel.PositionUnverified =
      !invalid && (unverified || fuel.PositionUnverified);
  }
}
