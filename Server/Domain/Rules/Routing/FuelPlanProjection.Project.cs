using System.Text.Json;
using Domain.Entities.Execution;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Reading a saved fuel plan back against the run as it stands now. What
// comes out is the plan the driver sees: the purchases still ahead, the
// miles already driven off it, and - when the run has moved past what the
// plan was built for - the reason it has to be worked out again.
public static partial class FuelPlanProjection
{
  public static FuelPlan Project(
    TruckFuelPlanSnapshot saved,
    RoutePlanningState state,
    IReadOnlyList<IWorkFacts> loads,
    RouteGeometry? currentLeg,
    DateTime now
  )
  {
    if (state.Plan is { } scopedPlan && !SameScope(saved, scopedPlan))
      return new FuelPlan
      {
        TruckId = scopedPlan.TruckId,
        ExecutionLegId = scopedPlan.ExecutionLegId,
        AssignmentRevision = scopedPlan.AssignmentRevision,
        NeedsRefresh = true,
        RefreshReasons = ["Execution changed. Recalculate fuel."],
      };
    var fuel = JsonSerializer.Deserialize<FuelPlan>(
      JsonSerializer.Serialize(saved.Plan, RoutingJson.Options),
      RoutingJson.Options
    )!;
    fuel.RefreshReasons = [];
    fuel.RouteChecks = [];
    fuel.StopArrivals = [];
    void Invalid(string reason)
    {
      fuel.NeedsRefresh = true;
      fuel.PricesOutOfDate = false;
      fuel.PositionUnverified = false;
      fuel.ScheduleImpact = null;
      fuel.RefreshReasons.Add(reason);
    }
    // The prices belong to an earlier pricing day. A recalculation is still
    // wanted, but the plan itself holds, so it stays readable until one
    // arrives and says why its cost is provisional.
    void Repriced(string reason)
    {
      if (!fuel.NeedsRefresh)
        fuel.PricesOutOfDate = true;
      fuel.NeedsRefresh = true;
      fuel.RefreshReasons.Add(reason);
    }
    // The position could not be read, so how far along he is is unknown -
    // but where he has to fuel is not. A recalculation is wanted; the stops
    // stay, because a driver with no fuel stop at all is the worse outcome.
    void Unverified(string reason)
    {
      if (!fuel.NeedsRefresh)
        fuel.PositionUnverified = true;
      fuel.NeedsRefresh = true;
      fuel.RefreshReasons.Add(reason);
    }
    fuel.NeedsRefresh = false;
    fuel.PricesOutOfDate = false;
    fuel.PositionUnverified = false;
    if (
      fuel.EstimatedStationAccess
      && fuel.SelectionVersion < MinimumEstimatedAccessVersion
    )
    {
      Invalid("Fuel station eligibility changed. Recalculate fuel.");
      return fuel;
    }
    var plan = state.Plan;
    if (
      plan is null
      || plan.TruckId != saved.TruckId
      || plan.InputsChanged
      || fuel.SelectionVersion < FuelOptimizer.MinimumProjectionVersion
      || fuel.SelectionVersion > FuelOptimizer.SelectionVersion
      || fuel.ProfileSignature
        != JsonSerializer.Serialize(state.Profile, RoutingJson.Options)
      || !AssignmentsMatch(fuel, plan.DispatchId, loads)
      || !RemainingStopsMatch(
        saved.Stops,
        plan.DispatchId,
        plan.Tracking.NextStopId,
        loads
      )
    )
    {
      Invalid("Assigned stops or fuel settings changed. Recalculate fuel.");
      return fuel;
    }
    if (fuel.PricingDate != FuelPricingDate.FromUtc(now))
      Repriced("Fuel prices need to be checked for today.");
    if (
      state.Progress
        is not { LocationStale: false, Position: { IsValid: true } position }
      || currentLeg is null
    )
    {
      Unverified(
        "The saved fuel road cannot be verified from the current GPS position."
      );
      return fuel;
    }
    var match = currentLeg.Match(position);
    if (
      match.Away
      > (
        fuel.EstimatedStationAccess
          ? FuelAccessEstimate.CurrentPositionToleranceMiles
          : .15
      )
    )
    {
      Invalid(
        "The saved fuel road cannot be verified from the current GPS position."
      );
      return fuel;
    }
    var stopIndex = saved
      .Stops.ToList()
      .FindIndex(x =>
        x.DispatchId == plan.DispatchId && x.Stop.Id == plan.Tracking.NextStopId
      );
    if (stopIndex < 0)
    {
      Invalid("The current stop changed. Recalculate fuel.");
      return fuel;
    }
    var along =
      (stopIndex == 0 ? 0 : saved.Stops[stopIndex - 1].EndMiles) + match.Along;
    var estimated = fuel.EstimatedStationAccess;
    if (
      estimated
      && fuel.Stops.Any(x =>
        x.RouteMilesAhead is not { } mile
        || !double.IsFinite(mile)
        || !double.IsFinite(x.DetourMiles)
        || x.DetourMiles < 0
      )
    )
    {
      Invalid("The saved station access estimate is incomplete.");
      return fuel;
    }
    double RouteMile(FuelPlanStop station) =>
      estimated ? station.RouteMilesAhead!.Value : station.MilesAhead;
    var currentAccessMiles = estimated
      ? FuelAccessEstimate.DistanceMiles(match.Away)
      : 0;
    for (var i = 0; i < fuel.Stops.Count; i++)
      if (fuel.Stops[i].Number <= 0)
        fuel.Stops[i].Number = i + 1;
    var remaining = fuel
      .Stops.Where(x =>
        FuelReservePolicy.PurchaseNotPassed(RouteMile(x), along)
      )
      .ToList();
    double gallons;
    if (
      fuel.ManualStartingFuel
      && (
        state.FuelUpdatedAt is null
        || state.FuelUpdatedAt <= (fuel.FuelObservedAt ?? fuel.CalculatedAt)
      )
      && now - fuel.CalculatedAt <= TimeSpan.FromMinutes(30)
      && fuel.Stops.All(x =>
        FuelReservePolicy.PurchaseNotPassed(RouteMile(x), along)
      )
    )
      gallons =
        fuel.StartingGallons
        - (along + Math.Max(0, fuel.StartAccessMiles - currentAccessMiles))
          / state.Profile.Mpg!.Value;
    // Match explicit calculation: reading age alone does not invalidate the
    // last reported level.
    else if (
      state.FuelPercent is >= 0 and <= 100
      && state.FuelUpdatedAt.HasValue
      && state.FuelUpdatedAt <= now.AddMinutes(1)
      && (
        !fuel.ManualStartingFuel
        || state.FuelUpdatedAt > (fuel.FuelObservedAt ?? fuel.CalculatedAt)
      )
    )
      gallons =
        state.FuelPercent.Value / 100 * state.Profile.TankGallons!.Value;
    else
    {
      Invalid("A fresh fuel reading or manual quantity is needed.");
      return fuel;
    }
    if (
      FuelReservePolicy.StartingLevelError(gallons, state.Profile) is
      { } levelError
    )
    {
      Invalid(levelError);
      return fuel;
    }
    fuel.Stops = remaining;
    fuel.StartingGallons = gallons;
    fuel.StartAccessMiles = currentAccessMiles;
    fuel.RemainingMiles =
      Math.Max(0, saved.Stops[^1].EndMiles - along)
      + fuel.StartAccessMiles
      + (estimated ? remaining.Sum(x => x.DetourMiles) : 0);
    var cursor = along;
    double previousAccessOut = fuel.StartAccessMiles,
      accessSoFar = fuel.StartAccessMiles;
    var cap =
      state.Profile.TankGallons!.Value * state.Profile.FillPercent / 100;
    var minimum = FuelReservePolicy.PhysicalArrivalMinimumGallons;
    foreach (var station in remaining)
    {
      var location = RouteMile(station);
      var accessIn = estimated ? station.DetourMiles / 2 : 0;
      gallons -=
        (Math.Max(0, location - cursor) + previousAccessOut + accessIn)
        / state.Profile.Mpg!.Value;
      station.ArrivalGallons = gallons;
      station.Warning = FuelReservePolicy.ArrivalWarning(
        gallons,
        state.Profile
      );
      if (gallons < minimum)
        Invalid(
          "A planned fuel stop is no longer reachable with the required fuel level."
        );
      if (station.FillToTarget)
        station.BuyGallons = Math.Max(0, cap - gallons);
      if (
        station.BuyGallons < FuelOptimizer.MinimumPurchaseGallons
        || gallons + station.BuyGallons > cap + 1e-9
      )
        Invalid("Purchase quantities need updating for the latest tank level.");
      gallons += station.BuyGallons;
      if (gallons < state.Profile.ReserveGallons)
        Invalid(
          "The first purchase does not restore the configured fuel reserve."
        );
      minimum = state.Profile.ReserveGallons;
      station.DepartureGallons = gallons;
      station.MilesAhead =
        Math.Max(0, location - along) + accessSoFar + accessIn;
      if (estimated)
        station.RouteMilesAhead = Math.Max(0, location - along);
      accessSoFar += accessIn * 2;
      previousAccessOut = accessIn;
      // The saved itinerary owns distance; current-dispatch map progress is a
      // different coordinate system.
      station.CurrentRouteMile = null;
      cursor = location;
    }
    gallons -=
      (Math.Max(0, saved.Stops[^1].EndMiles - cursor) + previousAccessOut)
      / state.Profile.Mpg!.Value;
    fuel.ArrivalGallons = gallons;
    if (
      gallons
      < (fuel.ArrivalPolicy?.MinimumGallons ?? state.Profile.ReserveGallons)
    )
      Invalid("The remaining plan does not preserve the arrival fuel reserve.");
    fuel.PurchaseGallons = remaining.Sum(x => x.BuyGallons);
    fuel.PurchaseCostUsd = remaining.Sum(x =>
      x.BuyGallons * x.CashUsdPerGallon
    );
    fuel.ExtraMinutes =
      FuelAccessEstimate.DrivingMinutes(fuel.StartAccessMiles)
      + remaining.Sum(x => x.DetourMinutes);
    fuel.EconomicCostUsd =
      remaining.Sum(x =>
        x.BuyGallons * x.EconomicUsdPerGallon + state.Profile.StopCostUsd
      )
      + fuel.ExtraMinutes / 60 * state.Profile.DriverHourlyCostUsd;
    fuel.ExpectedFutureFuelCostUsd =
      Math.Max(
        0,
        (fuel.ArrivalPolicy?.TargetGallons ?? 0) - fuel.ArrivalGallons
      ) * (fuel.ArrivalPolicy?.ReplacementPriceUsd ?? 0);
    fuel.RemainingCostEstimate = true;
    // Remaining access is priced; historical schedule wait/rest is not
    // replayed.
    fuel.SavingsUsd = null;
    fuel.RefreshReasons = fuel.RefreshReasons.Distinct().ToList();
    fuel.StopArrivals = FuelStopArrivals.Calculate(
      fuel,
      saved.Stops.Skip(stopIndex).ToArray(),
      state.Profile,
      along
    );
    fuel.ExecutionLegId = plan.ExecutionLegId;
    fuel.AssignmentRevision = plan.AssignmentRevision;
    return fuel;
  }
}
