using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Whether a new price is a reason to search again.
//
// A price moving at a station the plan buys at is worth a new search when
// it moves that purchase by at least what an extra fuel stop has to save.
// Less than that, the stations stay and only the estimate follows the new
// prices. A cheaper station somewhere else is looked for at the daily
// repricing and at any recalculation the plan needs anyway - not every time
// a price list lands, which would move the stops a driver was given.
public static class FuelPriceMateriality
{
  public sealed record Quote(
    double CashUsd,
    double EconomicUsd,
    double YourPrice,
    double EconomicPrice
  );

  public static bool Material(
    IEnumerable<FuelPlanStop> stops,
    Func<FuelPlanStop, Quote?> quote
  )
  {
    foreach (var stop in stops)
    {
      // A station no longer priced is not a price change: the plan cannot
      // stand as it is.
      if (quote(stop) is not { } now)
        return true;
      if (
        Math.Abs(now.EconomicUsd - stop.EconomicUsdPerGallon)
          * Math.Max(0, stop.BuyGallons)
        >= FuelStopEconomy.MinimumSavingsUsd
      )
        return true;
    }
    return false;
  }

  // The estimate follows the new prices; the stations and quantities stay.
  public static void Reprice(FuelPlan plan, Func<FuelPlanStop, Quote?> quote)
  {
    double cash = 0,
      economic = 0;
    foreach (var stop in plan.Stops)
    {
      if (quote(stop) is not { } now)
        continue;
      var gallons = Math.Max(0, stop.BuyGallons);
      cash += gallons * (now.CashUsd - stop.CashUsdPerGallon);
      economic += gallons * (now.EconomicUsd - stop.EconomicUsdPerGallon);
      stop.CashUsdPerGallon = now.CashUsd;
      stop.EconomicUsdPerGallon = now.EconomicUsd;
      stop.YourPrice = now.YourPrice;
      stop.EconomicPrice = now.EconomicPrice;
    }
    plan.PurchaseCostUsd += cash;
    plan.EconomicCostUsd += economic;
  }

  public static IReadOnlyDictionary<Guid, Quote> Quotes(
    IEnumerable<PricedFuelStation> priced
  ) =>
    priced
      .GroupBy(x => x.Station.StationId)
      .ToDictionary(
        x => x.Key,
        x =>
        {
          var best = x.MinBy(y => y.EconomicUsd)!;
          return new Quote(
            best.CashUsd,
            best.EconomicUsd,
            best.Station.YourPrice,
            best.Station.EconomicPrice
          );
        }
      );
}
