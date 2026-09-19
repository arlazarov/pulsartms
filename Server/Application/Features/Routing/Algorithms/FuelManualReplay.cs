using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class FuelManualReplay
{
  public const int MaximumStops = 40;
  private const double Tolerance = 1e-9;

  public static FuelManualReplayResult Evaluate(
    double routeMiles,
    double startingGallons,
    TruckRouteProfile profile,
    IReadOnlyList<FuelCandidate> visits,
    IReadOnlyList<FuelPlanEditStop> edits,
    FuelArrivalPolicy arrival,
    int routeVersion,
    double initialAccessMiles = 0
  )
  {
    var plan = new FuelPlan
    {
      SelectionVersion = FuelOptimizer.SelectionVersion,
      RouteVersion = routeVersion,
      EstimatedStationAccess = true,
      UsesIfta = profile.UseIfta,
      CalculatedAt = DateTime.UtcNow,
      StartingGallons = double.IsFinite(startingGallons) ? startingGallons : 0,
      StartAccessMiles = double.IsFinite(initialAccessMiles)
        ? initialAccessMiles
        : 0,
      RemainingMiles = double.IsFinite(routeMiles) ? routeMiles : 0,
    };
    var errors = new List<string>();
    var purchaseLimits = new double?[
      edits.Count <= MaximumStops ? edits.Count : 0
    ];
    FuelManualReplayResult Complete()
    {
      plan.NeedsRefresh = errors.Count > 0;
      plan.RefreshReasons = errors.Distinct().ToList();
      return new(plan, plan.RefreshReasons.ToList(), purchaseLimits);
    }

    if (profile.Validate(true) is { } profileError)
      errors.Add(profileError);
    if (!double.IsFinite(routeMiles) || routeMiles < 0)
      errors.Add(
        "The saved route mileage is invalid. Reload the route before editing fuel."
      );
    if (!double.IsFinite(initialAccessMiles) || initialAccessMiles < 0)
      errors.Add(
        "The estimated initial access distance is invalid. Reload the route before editing fuel."
      );
    if (visits.Count != edits.Count || visits.Count > MaximumStops)
      errors.Add(
        $"The fuel draft must contain matching station visits and no more than {MaximumStops} stops."
      );
    if (errors.Count > 0)
      return Complete();

    var mpg = profile.Mpg!.Value;
    var cap = profile.TankGallons!.Value * profile.FillPercent / 100;
    if (
      !double.IsFinite(arrival.MinimumGallons)
      || arrival.MinimumGallons < 0
      || !double.IsFinite(arrival.TargetGallons)
      || arrival.TargetGallons < arrival.MinimumGallons
      || !arrival.HasValidReplacementValue()
    )
      errors.Add(
        "Post-delivery fuel requirements are invalid. Reload the fuel plan."
      );
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < visits.Count; i++)
    {
      var candidate = visits[i];
      var edit = edits[i];
      if (
        candidate?.Station
          is not { StationId: var stationId, Point.IsValid: true }
        || stationId == Guid.Empty
        || edit is null
        || stationId != edit.StationId
        || edit.BeforeStopId is { } beforeStopId
          && beforeStopId != candidate.Station.BeforeStopId
        || candidate.LegIndex is < 0 or >= 40
        || !seen.Add(candidate.VisitKey)
      )
      {
        errors.Add(
          $"Fuel stop {i + 1} does not match a unique station visit on the assigned route."
        );
        continue;
      }
      if (
        !Nonnegative(candidate.AlongMiles)
        || candidate.AlongMiles > routeMiles
        || !Nonnegative(candidate.ExtraInMiles)
        || !Nonnegative(candidate.ExtraOutMiles)
        || !double.IsFinite(candidate.PriceUsd)
        || candidate.PriceUsd <= 0
        || !double.IsFinite(candidate.EconomicPriceUsd)
        || candidate.EconomicPriceUsd <= 0
        || !double.IsFinite(candidate.Station.YourPrice)
        || candidate.Station.YourPrice <= 0
        || !double.IsFinite(candidate.Station.EconomicPrice)
        || candidate.Station.EconomicPrice <= 0
        || !Nonnegative(edit.BuyGallons)
      )
        errors.Add(
          $"Fuel stop {i + 1} has invalid distance, price or purchase quantity. Reload or edit this stop."
        );
      if (
        i > 0
        && visits[i - 1] is { } previous
        && (
          candidate.AlongMiles < previous.AlongMiles
          || candidate.LegIndex < previous.LegIndex
          || previous.ExitMiles is { } exit
            && candidate.EntryMiles is { } entry
            && exit > entry
        )
      )
        errors.Add(
          $"Fuel stop {i + 1} is out of route order. Move it after the preceding route visits."
        );
      if (
        candidate.EntryMiles is { } inMile
          && (!Nonnegative(inMile) || inMile > candidate.AlongMiles)
        || candidate.ExitMiles is { } outMile
          && (
            !Nonnegative(outMile)
            || outMile < candidate.AlongMiles
            || outMile > routeMiles
          )
      )
        errors.Add(
          $"Fuel stop {i + 1} has invalid route access boundaries. Reload the route."
        );
    }
    if (errors.Count > 0)
      return Complete();

    plan.ArrivalPolicy = arrival;
    var levelError = FuelReservePolicy.StartingLevelError(
      startingGallons,
      profile
    );
    if (levelError is not null)
      errors.Add(levelError);
    var balancesKnown = levelError is null;
    var minimum = FuelReservePolicy.PhysicalArrivalMinimumGallons;
    var gallons = startingGallons;
    double cursor = 0,
      previousAccessOut = initialAccessMiles,
      accessSoFar = initialAccessMiles;
    var extraMinutes = FuelAccessEstimate.DrivingMinutes(initialAccessMiles);
    double purchaseGallons = 0,
      purchaseCost = 0,
      economicCost = extraMinutes / 60 * profile.DriverHourlyCostUsd;
    for (var i = 0; i < visits.Count; i++)
    {
      var candidate = visits[i];
      var edit = edits[i];
      var distance =
        candidate.AlongMiles
        - cursor
        + previousAccessOut
        + candidate.ExtraInMiles;
      var access = candidate.ExtraInMiles + candidate.ExtraOutMiles;
      var minutes = FuelAccessEstimate.DrivingMinutes(access);
      // Continuous replay matches the saved plan's normal measured-fuel
      // projection.
      gallons -= distance / mpg;
      var quantity = edit.FillToTarget
        ? Math.Max(0, cap - gallons)
        : edit.BuyGallons;
      var departure = gallons + quantity;
      var milesAhead =
        candidate.AlongMiles + accessSoFar + candidate.ExtraInMiles;
      var cash = quantity * candidate.PriceUsd;
      var economic =
        quantity * candidate.EconomicPriceUsd
        + profile.StopCostUsd
        + minutes / 60 * profile.DriverHourlyCostUsd;
      if (
        !double.IsFinite(gallons)
        || !double.IsFinite(departure)
        || !double.IsFinite(milesAhead)
        || !double.IsFinite(cash + purchaseCost)
        || !double.IsFinite(economic + economicCost)
        || !double.IsFinite(extraMinutes + minutes)
        || !double.IsFinite(accessSoFar + access)
      )
      {
        errors.Add(
          $"Fuel stop {i + 1} exceeds supported calculation limits. Check distance, prices and gallons."
        );
        return Complete();
      }
      balancesKnown &= gallons >= -Tolerance;
      if (balancesKnown)
        purchaseLimits[i] = Math.Clamp(cap - gallons, 0, cap);
      if (gallons + Tolerance < minimum)
        errors.Add(
          i == 0 && minimum == 0
            ? "Fuel stop 1 cannot be reached with the reported fuel. Confirm the level or arrange refueling before driving."
            : $"Fuel stop {i + 1} cannot be reached with the required reserve. Increase an earlier purchase or select a nearer station."
        );
      if (quantity + Tolerance < FuelOptimizer.MinimumPurchaseGallons)
        errors.Add(
          $"Fuel stop {i + 1} needs a purchase of at least {FuelOptimizer.MinimumPurchaseGallons} US gal. Adjust or remove it."
        );
      if (departure > cap + Tolerance)
        errors.Add(
          $"Fuel stop {i + 1} exceeds the configured fill limit. Reduce the purchase or choose Full tank."
        );
      if (departure + Tolerance < profile.ReserveGallons)
        errors.Add(
          $"Fuel stop {i + 1} does not restore the configured reserve. Increase the purchase."
        );
      var source = candidate.Station;
      plan.Stops.Add(
        new FuelPlanStop
        {
          Number = i + 1,
          VisitKey = candidate.VisitKey,
          DispatchId = source.DispatchId,
          BeforeStopId = source.BeforeStopId,
          CashUsdPerGallon = candidate.PriceUsd,
          EconomicUsdPerGallon = candidate.EconomicPriceUsd,
          StationId = source.StationId,
          Name = source.Name,
          Address = source.Address,
          Point = source.Point,
          MilesAhead = milesAhead,
          RouteMilesAhead = candidate.AlongMiles,
          ArrivalGallons = gallons,
          Warning = FuelReservePolicy.ArrivalWarning(gallons, profile),
          BuyGallons = quantity,
          DepartureGallons = departure,
          FillToTarget = edit.FillToTarget,
          YourPrice = source.YourPrice,
          EconomicPrice = source.EconomicPrice,
          Currency = source.Currency,
          Unit = source.Unit,
          DetourMiles = access,
          DetourMinutes = minutes,
          PriceDate = source.PriceDate,
        }
      );
      balancesKnown &= departure >= -Tolerance && departure <= cap + Tolerance;
      gallons = departure;
      cursor = candidate.AlongMiles;
      previousAccessOut = candidate.ExtraOutMiles;
      accessSoFar += access;
      extraMinutes += minutes;
      purchaseGallons += quantity;
      purchaseCost += cash;
      economicCost += economic;
      minimum = profile.ReserveGallons;
    }
    gallons -= (routeMiles - cursor + previousAccessOut) / mpg;
    var futureCost =
      Math.Max(0, arrival.TargetGallons - gallons)
      * arrival.ReplacementPriceUsd;
    if (
      !double.IsFinite(gallons)
      || !double.IsFinite(futureCost)
      || !double.IsFinite(routeMiles + accessSoFar)
    )
    {
      errors.Add(
        "The final fuel estimate exceeds supported calculation limits. Check the route and fuel requirements."
      );
      return Complete();
    }
    if (
      gallons + Tolerance
      < Math.Max(profile.ReserveGallons, arrival.MinimumGallons)
    )
      errors.Add(
        "The final arrival does not preserve the required fuel reserve. Increase a purchase or add a station."
      );
    plan.RemainingMiles = routeMiles + accessSoFar;
    plan.ArrivalGallons = gallons;
    plan.PurchaseGallons = purchaseGallons;
    plan.PurchaseCostUsd = purchaseCost;
    plan.EconomicCostUsd = economicCost;
    plan.ExpectedFutureFuelCostUsd = futureCost;
    plan.ExtraMinutes = extraMinutes;
    plan.Notes =
    [
      "Manual fuel quantities follow the saved route. Fuel readings and MPG are estimates.",
      "Station access distance and time are estimates, not verified truck approaches.",
      arrival.Reason,
    ];
    return Complete();
  }

  private static bool Nonnegative(double value) =>
    double.IsFinite(value) && value >= 0;
}
