using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

public static partial class FuelOptimizer
{
  public const int SelectionVersion = 32;

  // Search/ranking upgrades preserve the validity of previously checked roads.
  public const int MinimumProjectionVersion = 11;
  public const int MinimumPurchaseGallons = 10;
  public const int MinimumAutomaticPurchaseGallons = 25;

  private sealed record Purchase(FuelPlanStop Stop, FuelCandidate Candidate);

  private sealed record State(
    double Cost,
    double Fuel,
    List<Purchase> Purchases
  );

  public static FuelPlan Optimize(
    double miles,
    double currentGallons,
    TruckRouteProfile profile,
    IReadOnlyList<FuelCandidate> candidates,
    int routeVersion,
    bool useIfta,
    bool fewestStops = false,
    bool compare = true,
    FuelArrivalPolicy? arrivalPolicy = null,
    double initialAccessMiles = 0
  ) =>
    OptimizeWithVisits(
      miles,
      currentGallons,
      profile,
      candidates,
      routeVersion,
      useIfta,
      fewestStops,
      compare,
      arrivalPolicy,
      initialAccessMiles
    ).Plan;

  public static (
    FuelPlan Plan,
    List<FuelCandidate> Purchases
  ) OptimizeWithVisits(
    double miles,
    double currentGallons,
    TruckRouteProfile profile,
    IReadOnlyList<FuelCandidate> candidates,
    int routeVersion,
    bool useIfta,
    bool fewestStops = false,
    bool compare = true,
    FuelArrivalPolicy? arrivalPolicy = null,
    double initialAccessMiles = 0
  )
  {
    var (
      mpg,
      cap,
      reserve,
      firstMinimum,
      arrivalMinimum,
      fillBeforeUnknownArea,
      hasUsableExit,
      stations,
      canFillBeforeDelivery,
      topUpThreshold,
      topUpStation
    ) = FuelSearchSetup.From(
      miles,
      currentGallons,
      profile,
      candidates,
      arrivalPolicy,
      initialAccessMiles
    );
    double FinalScore(State state) =>
      state.Cost
      + (
        arrivalPolicy is null
          ? 0
          : Math.Max(0, arrivalPolicy.TargetGallons - state.Fuel)
            * arrivalPolicy.ReplacementPriceUsd
      );
    var buckets = Enumerable
      .Range(0, stations.Count + 2)
      .Select(_ => new Dictionary<int, List<State>>())
      .ToList();
    buckets[0][(int)Math.Floor(currentGallons)] = [new(0, currentGallons, [])];
    for (var i = 0; i <= stations.Count; i++)
    {
      foreach (var incoming in buckets[i].Values.SelectMany(x => x).ToList())
      {
        var from = i == 0 ? null : stations[i - 1];
        if (
          arrivalPolicy is not { EconomicPurchasesOnly: true }
          && from is not null
          && incoming.Fuel > topUpThreshold
          && incoming.Fuel
            - (miles - from.AlongMiles + from.ExtraOutMiles) / mpg
            >= arrivalMinimum
        )
          continue;
        foreach (
          var quantity in from is null
            ? new[] { 0d }
            : PurchaseQuantities(cap - incoming.Fuel)
        )
        {
          var departure = incoming.Fuel + quantity;
          var full = from is not null && Math.Abs(departure - cap) < 1e-9;
          if (from is not null && departure < reserve)
            continue;
          var stopCost = from is null
            ? 0
            : quantity * from.EconomicPriceUsd
              + profile.StopCostUsd
              + Math.Max(0, from.Station.DetourMinutes)
                / 60
                * profile.DriverHourlyCostUsd;
          List<Purchase>? stops = null;
          for (var j = i + 1; j < buckets.Count; j++)
          {
            var to = j > stations.Count ? null : stations[j - 1];
            // Prefer filling here over a dearer optional stop when the horizon
            // and known exit fit.
            // Test capacity, not this purchase quantity, so a smaller fill
            // cannot evade the rule.
            if (
              hasUsableExit
              && from is not null
              && to is not null
              && to.EconomicPriceUsd > from.EconomicPriceUsd
              && cap - (miles - from.AlongMiles + from.ExtraOutMiles) / mpg
                >= arrivalMinimum
            )
              continue;
            // Overlapping alternative paths cannot be added as independent
            // detours.
            if (
              from?.ExitMiles is { } exit
              && to?.EntryMiles is { } entry
              && exit > entry
            )
              continue;
            // The last purchase before an uncertain poor-fuel destination must
            // fill to the configured limit.
            if (
              to is null
              && fillBeforeUnknownArea
              && !full
              && (
                from is not null
                || incoming.Fuel <= topUpThreshold && canFillBeforeDelivery
              )
            )
              continue;
            if (
              from == topUpStation
              && topUpStation is not null
              && incoming.Fuel <= topUpThreshold
              && !full
            )
              continue;
            if (
              topUpStation is not null
              && (from?.AlongMiles ?? -1) < topUpStation.AlongMiles
              && (to?.AlongMiles ?? miles) > topUpStation.AlongMiles
            )
            {
              var fuelAtTopUp =
                departure
                - (
                  topUpStation.AlongMiles
                  - (from?.AlongMiles ?? 0)
                  + (from?.ExtraOutMiles ?? initialAccessMiles)
                  + topUpStation.ExtraInMiles
                ) / mpg;
              // Do not skip the last substantially cheaper station when a
              // useful top-up is reachable.
              if (
                fuelAtTopUp >= reserve
                && fuelAtTopUp <= topUpThreshold
                && cap - fuelAtTopUp >= MinimumAutomaticPurchaseGallons
                && (
                  from is null
                  || topUpStation.AlongMiles - from.AlongMiles >= 10
                )
              )
                continue;
            }
            if (
              from is not null
              && to is not null
              && to.AlongMiles - from.AlongMiles < 10
            )
              continue;
            var distance =
              (to?.AlongMiles ?? miles)
              - (from?.AlongMiles ?? 0)
              + (from?.ExtraOutMiles ?? initialAccessMiles)
              + (to?.ExtraInMiles ?? 0);
            // Keep consumption continuous: rounding each leg changes station
            // rankings and invents purchase deficits.
            var fuelNeeded = Math.Max(0, distance) / mpg;
            var arrival = departure - fuelNeeded;
            if (
              arrival
              < (
                to is null ? arrivalMinimum
                : from is null ? firstMinimum
                : reserve
              )
            )
              continue;
            var cost = incoming.Cost + stopCost;
            var key = (int)Math.Floor(arrival);
            if (!buckets[j].TryGetValue(key, out var alternatives))
              buckets[j][key] = alternatives = [];
            var count = incoming.Purchases.Count + (from is null ? 0 : 1);
            if (Dominates(alternatives, arrival, cost, count, fewestStops))
              continue;
            // Every destination shares this immutable purchase prefix.
            if (stops is null)
            {
              stops = incoming.Purchases.ToList();
              if (from is not null)
                stops.Add(
                  new(
                    new FuelPlanStop
                    {
                      StationId = from.Station.StationId,
                      Name = from.Station.Name,
                      Address = from.Station.Address,
                      Point = from.Station.Point,
                      MilesAhead = from.AlongMiles + from.ExtraInMiles,
                      ArrivalGallons = incoming.Fuel,
                      BuyGallons = quantity,
                      DepartureGallons = departure,
                      FillToTarget = full,
                      YourPrice = from.Station.YourPrice,
                      EconomicPrice = from.Station.EconomicPrice,
                      Currency = from.Station.Currency,
                      Unit = from.Station.Unit,
                      DetourMiles = Math.Max(
                        0,
                        from.ExtraInMiles + from.ExtraOutMiles
                      ),
                      DetourMinutes = from.Station.DetourMinutes,
                      PriceDate = from.Station.PriceDate,
                      EstimatedArrival = from.Station.EstimatedArrival,
                      PriceEstimated = from.Station.PriceEstimated,
                    },
                    from
                  )
                );
            }
            alternatives.Add(new(cost, arrival, stops));
            // Bound fractional alternatives by cash, terminal value and range;
            // never round a retained balance or its cost.
            // This is a bounded local search, not a proof of a global optimum
            // across every possible fuel balance.
            RetainAlternatives(alternatives, fewestStops, arrivalPolicy);
          }
        }
      }
    }
    var best =
      buckets[^1]
        .Values.SelectMany(x => x)
        .OrderBy(x => fewestStops ? x.Purchases.Count : 0)
        .ThenBy(FinalScore)
        .FirstOrDefault()
      ?? throw new RoutePlanningException(
        "No fuel plan can maintain the reserve using the verified BVD stations. Review fuel level, MPG or the permitted detour; do not rely on these stations to complete the trip."
      );
    var cash = best.Purchases.Sum(x =>
      x.Stop.BuyGallons * x.Candidate.PriceUsd
    );
    var purchasedStops = best.Purchases.Select(x => x.Stop).ToList();
    for (var i = 0; i < purchasedStops.Count; i++)
      purchasedStops[i].Number = i + 1;
    if (purchasedStops.FirstOrDefault() is { } first)
      first.Warning = FuelReservePolicy.ArrivalWarning(
        first.ArrivalGallons,
        profile
      );
    var economicCost = best.Cost;
    var futureCost = arrivalPolicy is null
      ? 0
      : Math.Max(0, arrivalPolicy.TargetGallons - best.Fuel)
        * arrivalPolicy.ReplacementPriceUsd;
    var baseline = compare
      ? Optimize(
        miles,
        currentGallons,
        profile,
        candidates,
        routeVersion,
        useIfta,
        true,
        false,
        arrivalPolicy,
        initialAccessMiles
      )
      : null;
    return (
      new()
      {
        SelectionVersion = SelectionVersion,
        ArrivalPolicy = arrivalPolicy,
        ExpectedFutureFuelCostUsd = futureCost,
        SavingsUsd = baseline is null
          ? null
          : Math.Max(
            0,
            baseline.EconomicCostUsd
              + baseline.ExpectedFutureFuelCostUsd
              - economicCost
              - futureCost
          ),
        CalculatedAt = DateTime.UtcNow,
        RouteVersion = routeVersion,
        StartingGallons = currentGallons,
        RemainingMiles = miles + initialAccessMiles,
        ArrivalGallons = best.Fuel,
        PurchaseGallons = purchasedStops.Sum(x => x.BuyGallons),
        PurchaseCostUsd = cash,
        EconomicCostUsd = economicCost,
        ExtraMinutes = purchasedStops.Sum(x => x.DetourMinutes),
        UsesIfta = useIfta,
        Stops = purchasedStops,
        Notes =
        [
          "Automatic purchases start at 25 US gal, then use 10-gallon steps or the exact fill target. Consumption preserves fractional gallons; MPG and fuel readings are estimates.",
          arrivalPolicy?.Reason
            ?? "Plan covers the remaining load only. Prices must be checked again before purchasing.",
        ],
      },
      best.Purchases.Select(x => x.Candidate).ToList()
    );
  }

  private static IEnumerable<double> PurchaseQuantities(double headroom)
  {
    if (headroom < MinimumAutomaticPurchaseGallons)
      yield break;
    yield return MinimumAutomaticPurchaseGallons;
    for (var quantity = 30; quantity < headroom - 1e-9; quantity += 10)
      yield return quantity;
    if (headroom > MinimumAutomaticPurchaseGallons)
      yield return headroom;
  }
}
