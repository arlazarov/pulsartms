using System.Text.Json;
using Application.Features.Fuel.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Execution;

namespace Application.Features.Routing.Algorithms;

public static class FuelPlanProjection
{
  private const int MinimumEstimatedAccessVersion = 28;

  public static bool SameScope(TruckFuelPlanSnapshot saved, RoutePlan plan)
  {
    if (
      saved.TruckId != plan.TruckId
      || saved.Plan.ExecutionLegId != saved.RootExecutionLegId
      || saved.Plan.AssignmentRevision != saved.AssignmentRevision
    )
      return false;
    if (saved.RootDispatchId == plan.DispatchId)
      return saved.RootExecutionLegId == plan.ExecutionLegId
        && saved.AssignmentRevision == plan.AssignmentRevision;
    var scope = saved
      .Stops.Where(x => x.DispatchId == plan.DispatchId)
      .ToArray();
    return scope.Length > 0
      && scope.All(x =>
        x.ExecutionLegId == plan.ExecutionLegId
        && x.AssignmentRevision == plan.AssignmentRevision
      );
  }

  public static bool RemainingStopsMatch(
    IReadOnlyList<FuelItineraryStop> itinerary,
    Guid currentDispatchId,
    Guid? nextStopId,
    IReadOnlyList<IWorkFacts> loads
  )
  {
    var index = itinerary
      .ToList()
      .FindIndex(x =>
        x.DispatchId == currentDispatchId && x.Stop.Id == nextStopId
      );
    if (index < 0)
      return false;
    var scope = itinerary[index];
    var current = loads
      .Where(x =>
        x.Id == currentDispatchId
        && x.ExecutionLegId == scope.ExecutionLegId
        && x.AssignmentRevision == scope.AssignmentRevision
      )
      .ToArray();
    if (current.Length != 1)
      return false;
    if (
      !TrySelectLoads(
        new()
        {
          TruckId = current[0].TruckId ?? Guid.Empty,
          DispatchId = currentDispatchId,
          ExecutionLegId = scope.ExecutionLegId,
          AssignmentRevision = scope.AssignmentRevision,
        },
        loads,
        out var selected
      )
    )
      return false;
    var byLoad = selected.ToDictionary(x => x.Id);
    foreach (var group in itinerary.Skip(index).GroupBy(x => x.DispatchId))
    {
      if (
        !byLoad.TryGetValue(group.Key, out var load)
        || group.Any(x =>
          x.ExecutionLegId != load.ExecutionLegId
          || x.AssignmentRevision != load.AssignmentRevision
        )
      )
        return false;
      var remaining = load
        .Stops.Where(x => !x.DriverOnly)
        .OrderBy(x => x.Sequence)
        .AsEnumerable();
      // Current-route progress may have passed a prefix before actual events
      // arrive.
      if (group.Key == currentDispatchId)
        remaining = remaining.SkipWhile(x => x.Id != nextStopId);
      if (
        !remaining
          .Where(x => !x.IsCompleted)
          .Select(x => x.Id)
          .SequenceEqual(group.Select(x => x.Stop.Id))
      )
        return false;
    }
    return true;
  }

  public static bool AssignmentsMatch(
    FuelPlan fuel,
    Guid currentDispatchId,
    IReadOnlyList<IWorkFacts> loads
  )
  {
    var root = currentDispatchId == fuel.DispatchIds.FirstOrDefault();
    if (
      !TrySelectLoads(
        new()
        {
          TruckId = fuel.TruckId,
          DispatchId = currentDispatchId,
          ExecutionLegId = root ? fuel.ExecutionLegId : null,
          AssignmentRevision = root ? fuel.AssignmentRevision : 0,
        },
        loads,
        out var selected
      )
    )
      return false;
    loads = selected;
    var index = fuel.DispatchIds.IndexOf(currentDispatchId);
    var current = loads.ToList().FindIndex(x => x.Id == currentDispatchId);
    if (index < 0 || current < 0)
      return false;
    var expected = fuel.DispatchIds.Skip(index).ToArray();
    var actual = loads.Skip(current).Take(expected.Length).ToArray();
    if (!actual.Select(x => x.Id).SequenceEqual(expected))
      return false;
    if (
      actual.Any(load =>
        !fuel.DispatchSignatures.TryGetValue(load.Id, out var signature)
        || signature != FuelHorizon.LoadSignature(load)
      )
    )
      return false;
    var next = loads.Skip(current + expected.Length).FirstOrDefault();
    return next?.Id == fuel.ArrivalPolicy?.NextDispatchId
      && (
        next is null
        || fuel.DispatchSignatures.TryGetValue(next.Id, out var nextSignature)
          && nextSignature == FuelHorizon.LoadSignature(next)
      );
  }

  private static bool TrySelectLoads(
    RoutePlan plan,
    IReadOnlyList<IWorkFacts> loads,
    out List<IWorkFacts> selected
  )
  {
    try
    {
      selected = FuelHorizon.SelectLoads(plan, loads);
      return true;
    }
    catch (RoutePlanningException)
    {
      selected = [];
      return false;
    }
  }

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
      JsonSerializer.Serialize(saved.Plan, RoutePlanningService.Json),
      RoutePlanningService.Json
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
        != JsonSerializer.Serialize(state.Profile, RoutePlanningService.Json)
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
