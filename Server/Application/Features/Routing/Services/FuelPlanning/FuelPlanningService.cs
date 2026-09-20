using System.Text.Json;
using Application.Caching;
using Application.Diagnostics;
using Application.Features.Eta.Interfaces;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelPlanningService(
  IPlannedRouteReader plans,
  IFuelWorkInputsReader inputs,
  ICarrierFuelPrices fuelPrices,
  FuelRegionPlanner regions,
  FuelHorizon horizons,
  IOptions<FuelRegionOptions> options,
  TruckFuelPlans savedPlans,
  FuelScheduleEvaluator schedules,
  IRouteRegionLookup regionLookup,
  PlanningWorkPublication publication,
  TruckPlanningProfileService profiles,
  RoutePlanStore routeStore,
  ISavedRoadValidation roads
)
{
  private static readonly KeyedGates TruckGates = new();

  // Manual searches share a small per-process budget; ordinary planning reads
  // never acquire it.
  private static readonly SemaphoreSlim SearchSlots = new(2, 2);

  public async Task<FuelCalculationResult> BuildAsync(
    Guid dispatchId,
    FuelBuildRequest request,
    CancellationToken ct
  )
  {
    var assignedLoad = await FuelLoadAsync(
      dispatchId,
      request.ExecutionLegId,
      request.AssignmentRevision,
      ct
    );
    var truckId =
      assignedLoad.TruckId
      ?? throw new RoutePlanningException(
        "A truck assignment is required for fuel planning."
      );
    var gate = TruckGates.For(truckId);
    await GateWait.WaitAsync(gate, "FuelTruck", ct);
    try
    {
      await GateWait.WaitAsync(SearchSlots, "FuelSearch", ct);
      try
      {
        return await BuildCoreAsync(dispatchId, request, ct);
      }
      finally
      {
        SearchSlots.Release();
      }
    }
    finally
    {
      gate.Release();
    }
  }

  private async Task<FuelCalculationResult> BuildCoreAsync(
    Guid dispatchId,
    FuelBuildRequest request,
    CancellationToken ct,
    bool replaceManual = false,
    DateTime? expectedCalculatedAt = null
  )
  {
    var config = options.Value;
    var assignedLoad = await FuelLoadAsync(
      dispatchId,
      request.ExecutionLegId,
      request.AssignmentRevision,
      ct
    );
    var captured = await inputs.ReadFreshAsync(assignedLoad.TruckId!.Value, ct);
    assignedLoad = captured.Root(
      new()
      {
        TruckId = assignedLoad.TruckId.Value,
        DispatchId = assignedLoad.Id,
        ExecutionLegId = assignedLoad.ExecutionLegId,
        AssignmentRevision = assignedLoad.AssignmentRevision,
      }
    );
    var state = await plans.GetAsync(assignedLoad, ct);
    var existing = await savedPlans.ReadUncachedAsync(
      state.Plan?.TruckId ?? assignedLoad.TruckId!.Value,
      ct
    );
    if (request.AutomaticRefreshRevision is { } refreshRevision)
    {
      RequireRevision(existing, refreshRevision);
      if (existing!.Plan.ManuallyEdited || existing.Plan.ManualStartingFuel)
        throw new PlanningSettingsConflictException(
          "Manual fuel plans are not refreshed automatically."
        );
    }
    if (replaceManual)
    {
      if (
        existing is null
        || expectedCalculatedAt.HasValue
        || state.Plan is { } currentPlan
          && FuelPlanProjection.SameScope(existing, currentPlan)
      )
        RequireRevision(existing, expectedCalculatedAt);
      else
        // A plan from the truck's previous execution is not the opened draft.
        // Keep its database revision only as the compare-and-swap token.
        expectedCalculatedAt = existing.CalculatedAt;
    }
    else if (
      existing?.Plan.ManuallyEdited == true
      && state.Plan is { } existingState
      && FuelPlanProjection.SameScope(existing, existingState)
    )
      throw new RoutePlanningException(
        "This plan has manual changes. Use Edit fuel plan or explicitly reset it to automatic."
      );
    var p = request.Profile;
    PlanningPreferences.From(state.Profile).ApplyTo(p);
    p.UsesFleetDefaults = state.Profile.UsesFleetDefaults;
    if (p.Validate(true) is { } error)
      throw new RoutePlanningException(error);
    var plan =
      state.Plan
      ?? throw new RoutePlanningException("Build the truck route first.");
    if (plan.ExecutionLegId.HasValue)
      await RequireCurrentAsync(
        captured,
        dispatchId,
        plan.ExecutionLegId,
        plan.AssignmentRevision,
        state.Profile,
        ct
      );
    if (plan.InputsChanged || !SameVehicle(p, plan.Profile))
      throw new RoutePlanningException(
        "The truck or stops changed. Rebuild the route before planning fuel."
      );
    if (
      state.Progress?.RemainingMiles is not { } remaining
      || state.Progress.ProgressMiles is null
      || state.Progress.Position?.IsValid != true
      || state.Progress.LocationStale
    )
      throw new RoutePlanningException(
        "Fuel quantities will update when a fresh GPS position is available on the current route."
      );
    double gallons;
    var manual = request.CurrentGallons.HasValue;
    if (manual)
      gallons = request.CurrentGallons!.Value;
    else
    {
      if (
        state.FuelPercent is not { } percent
        || !double.IsFinite(percent)
        || percent is < 0 or > 100
        || state.FuelUpdatedAt is null
        || state.FuelUpdatedAt > DateTime.UtcNow.AddMinutes(1)
      )
        throw new RoutePlanningException(
          "No valid fuel level is available for this truck."
        );
      gallons = p.TankGallons!.Value * percent / 100;
    }
    if (FuelReservePolicy.StartingLevelError(gallons, p) is { } levelError)
      throw new RoutePlanningException(levelError);
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    var response =
      await fuelPrices.ReadAsync(today, ct)
      ?? throw new RoutePlanningException(
        "Fuel prices are temporarily unavailable."
      );
    var unpricedStations = new List<PricedFuelStation>();
    var prices = FuelRegionGrid.Prices(response, p, today, unpricedStations);
    var priceSignature = FuelPriceSignature.From(prices);
    var loads = captured.Select(plan);
    var assignmentSignatures = loads.ToDictionary(
      x => x.Id,
      FuelHorizon.LoadSignature
    );
    var horizon = await horizons.BuildAsync(state, p, ct, captured);
    // Nothing left to drive is nothing to fuel. A truck standing on the last
    // stop of its last load has a route of zero miles, which is not a route:
    // the store refuses to keep a plan against one, and refused it by
    // throwing - so pressing "Calculate automatically" on 11006, parked at
    // its delivery with a full tank, answered a dispatcher with the name of
    // an exception class.
    if (!(horizon.Route.Miles > 0))
      throw new RoutePlanningException(
        "There is no distance left on this assignment to plan fuel for."
      );
    var terminal = new RoutePlan
    {
      TruckId = plan.TruckId,
      DispatchId = horizon.DispatchIds[^1],
      ExecutionLegId = horizon.Itinerary[^1].ExecutionLegId,
      AssignmentRevision = horizon.Itinerary[^1].AssignmentRevision,
      Route = horizon.Route,
    };
    var geometry = new FuelSearchGeometry(horizon.Route, ct);
    var scheduleState = state with
    {
      Progress = state.Progress! with
      {
        Position = horizon.Route.Legs[0].Points[0],
      },
    };
    var schedule = await schedules.PrepareAsync(
      scheduleState,
      horizon.Route,
      horizon.Itinerary,
      DateTime.UtcNow,
      ct
    );
    var calendar = new FuelPriceCalendar(fuelPrices, today, response);
    remaining = horizon.Route.Miles;
    var corridorStations = new HashSet<Guid>();
    var countries = new FuelAccessCountries(regionLookup);
    List<FuelCandidate> shortList;
    List<FuelCandidate> reachable;
    using (PerformanceStages.Start("fuel", "station-matching"))
    {
      List<FuelCandidate> Match(IReadOnlyList<PricedFuelStation> stations) =>
        FuelAccessEstimate.Nearby(
          FuelRouteOccurrences
            .Create(
              horizon.Route,
              stations,
              config,
              ct,
              geometry,
              FuelAccessEstimate.NearbyMiles,
              corridorStations
            )
            .Where(x =>
              countries.Matches(geometry.At(x.AlongMiles, ct), x.Station)
            )
        );
      shortList = Match(prices);
      // Cost comparison sees priced stations only. Reachability sees every
      // station on the road, so a driver is told where he can get to even
      // where no price is known.
      reachable =
        unpricedStations.Count == 0
          ? shortList
          : [.. shortList, .. Match(unpricedStations)];
    }
    shortList = await calendar.PriceAsync(
      shortList,
      schedule.Arrivals(
        horizon.Route,
        shortList,
        geometry,
        horizon.StartAccessMiles,
        false,
        ct
      ),
      p,
      ct
    );
    var arrivalInputs = await regions.BuildAsync(
      terminal,
      p,
      prices,
      0,
      ct,
      geometry,
      captured,
      corridorStations
    );

    var arrival = arrivalInputs.Policy;
    var selected = FuelRouteSearch.SelectCandidates(
      shortList,
      remaining,
      gallons,
      p,
      arrival,
      config,
      includeAccess: true,
      initialAccessMiles: horizon.StartAccessMiles
    );
    var stops = horizon.Stops;
    if (stops.Count == 0)
      throw new RoutePlanningException("No remaining dispatch stops.");
    var baseline = horizon.Route;
    var initialMinutes = FuelAccessEstimate.DrivingMinutes(
      horizon.StartAccessMiles
    );
    var optimization = new FuelOptimizationMemo(
      remaining,
      gallons,
      p,
      arrival,
      plan.Version,
      horizon.StartAccessMiles
    );
    var chains = FuelZoneSearch.Schedule(
      FuelRouteSearch.Chains(
        selected,
        remaining,
        gallons,
        p,
        arrival,
        ct,
        includeAccess: true,
        initialAccessMiles: horizon.StartAccessMiles,
        memo: optimization
      ),
      FuelZoneSearch.Representatives(selected, config)
    );
    FuelPlan? bestFuel = null;
    List<FuelCandidate> bestPurchases = [];
    (int Cycle, int Unknown, int Late) bestImpact = (
      int.MaxValue,
      int.MaxValue,
      int.MaxValue
    );
    double bestScore = double.PositiveInfinity;
    var evaluatedRoutes = 0;
    var seen = new HashSet<string>();
    var checks = new List<FuelRouteCheck>();
    FuelRouteCheck? winner = null;
    var comparisonLimit = Math.Min(
      config.CandidateRoadChecks,
      horizon.StartAccessMiles > 0 ? 11 : 12
    );
    foreach (var chain in chains)
    {
      if (evaluatedRoutes >= comparisonLimit)
        break;
      var ordered = chain.OrderBy(x => x.AlongMiles).ToList();
      ordered = await calendar.PriceAsync(
        ordered,
        schedule.Arrivals(
          baseline,
          ordered,
          geometry,
          horizon.StartAccessMiles,
          true,
          ct
        ),
        p,
        ct
      );
      if (!seen.Add(string.Join(",", ordered.Select(x => x.VisitKey))))
        continue;
      var check = new FuelRouteCheck
      {
        Stations = ordered.Select(x => x.Station.Name).ToList(),
      };
      ct.ThrowIfCancellationRequested();
      evaluatedRoutes++;
      checks.Add(check);
      var extraMiles =
        horizon.StartAccessMiles
        + ordered.Sum(x => x.ExtraInMiles + x.ExtraOutMiles);
      var extraMinutes =
        initialMinutes + ordered.Sum(x => x.Station.DetourMinutes);
      check.ExtraMiles = extraMiles;
      check.ExtraMinutes = extraMinutes;
      FuelPlan fuel;
      List<FuelCandidate> purchases;
      try
      {
        (fuel, purchases) = optimization.Take(ordered);
      }
      catch (RoutePlanningException)
      {
        check.Result = "Cannot preserve fuel reserve";
        continue;
      }
      // Do not keep an unnecessary waypoint when the optimizer buys nothing
      // there.
      if (fuel.Stops.Count != ordered.Count)
      {
        check.Result = "Includes a station without a useful purchase";
        continue;
      }
      fuel.EconomicCostUsd += initialMinutes / 60 * p.DriverHourlyCostUsd;
      if (
        FuelScheduleRanking.CanSkipReplay(
          fuel,
          0,
          p.DriverHourlyCostUsd,
          bestFuel is null ? null : bestImpact,
          bestScore,
          bestFuel?.Stops.Count ?? 0
        )
      )
      {
        check.Result = "Higher estimated cost before schedule replay";
        continue;
      }
      fuel.ScheduleImpact =
        chain.Count == 0 && horizon.StartAccessMiles == 0
          ? schedule.Baseline
          : schedule.Evaluate(
            FuelAccessEstimate.TimingRoute(
              baseline,
              purchases,
              horizon.StartAccessMiles
            ),
            ct
          );
      var impact = FuelScheduleRanking.For(fuel.ScheduleImpact);
      // Access driving time is already priced by the optimizer; add only
      // further schedule delay.
      var timeCost = Math.Max(
        0,
        FuelScheduleRanking.DelayCost(
          fuel.ScheduleImpact,
          extraMiles,
          extraMinutes,
          p.DriverHourlyCostUsd
        )
          - extraMinutes / 60 * p.DriverHourlyCostUsd
      );
      var score =
        fuel.EconomicCostUsd + fuel.ExpectedFutureFuelCostUsd + timeCost;
      check.CostUsd = score;
      check.Result = "Higher total cost";
      if (impact.CompareTo(bestImpact) > 0)
      {
        check.Result = "Worse schedule feasibility";
        continue;
      }
      if (
        impact.CompareTo(bestImpact) == 0
        && FuelStopEconomy.Compare(
          score,
          fuel.Stops.Count,
          bestScore,
          bestFuel!.Stops.Count
        ) >= 0
      )
      {
        if (score < bestScore)
          check.Result = "Additional stops save less than $20 each";
        continue;
      }
      fuel.ExtraMinutes = extraMinutes;
      fuel.EconomicCostUsd += timeCost;
      fuel.SavingsUsd = null;
      winner = check;
      bestScore = score;
      bestFuel = fuel;
      bestPurchases = purchases;
      bestImpact = impact;
    }
    PerformanceStages.Count(
      "fuel",
      "optimizer-calculations",
      optimization.Calculations
    );
    PerformanceStages.Count("fuel", "optimizer-reuses", optimization.Reuses);
    if (bestFuel is null)
      return FuelCalculationResult.Diagnostic(
        await ReportFuelAccessAsync(
          state,
          captured,
          reachable,
          gallons,
          horizon.StartAccessMiles,
          p,
          arrivalInputs.History is { } accessHistory
            ? horizon.History.Add(accessHistory)
            : horizon.History,
          arrivalInputs.Road is { } accessRoad
            ? horizon.Roads.Add(accessRoad)
            : horizon.Roads,
          ct
        )
      );
    if (
      bestFuel.Stops.FirstOrDefault() is { } firstPurchase
      && firstPurchase.ArrivalGallons < p.ReserveGallons
    )
    {
      firstPurchase.Warning = FuelReservePolicy.ArrivalWarning(
        firstPurchase.ArrivalGallons,
        p
      );
      bestFuel.Notes.Add(firstPurchase.Warning);
    }
    winner!.Result = "Selected";
    bestFuel.RouteChecks = checks;
    bestFuel.UsDiscountSignature = calendar.Signature;
    bestFuel.PriceDates = calendar.Dates;
    if (bestFuel.Stops.Any(x => x.PriceEstimated))
      bestFuel.Notes.Add(
        "Prices without a published arrival-date quote are estimated using today's available price; unknown arrival times also use today's price."
      );
    bestFuel.EstimatedStationAccess = true;
    bestFuel.StartAccessMiles = horizon.StartAccessMiles;
    bestFuel.RemainingMiles =
      baseline.Miles
      + horizon.StartAccessMiles
      + bestFuel.Stops.Sum(x => x.DetourMiles);
    bestFuel.Notes.Add(
      $"Compared {evaluatedRoutes} fuel chains on the saved route without routing requests. Station access distance and time are estimates, not verified truck approaches."
    );
    bestFuel.Notes.AddRange(horizon.Notes);
    bestFuel.Notes.Add(
      "Additional fuel stops must save at least $20 each against a feasible alternative with fewer stops and the same schedule rank. This is a selection threshold, not a stop charge."
    );
    var committed = await CommitAsync(
      bestFuel,
      bestPurchases,
      baseline,
      horizon.Itinerary,
      horizon.DispatchIds,
      horizon.DispatchSignatures,
      horizon.AssignmentSignature,
      assignmentSignatures,
      arrivalInputs.History is { } history
        ? horizon.History.Add(history)
        : horizon.History,
      arrivalInputs.Road is { } road ? horizon.Roads.Add(road) : horizon.Roads,
      captured,
      state,
      p,
      manual,
      priceSignature,
      today,
      existing?.CalculatedAt,
      ct
    );
    return FuelCalculationResult.Feasible(committed, p.ReserveGallons);
  }

  // Opens the publication for the captured work and confirms, inside it, that
  // every input the result was calculated from is still current: the truck
  // itinerary, the saved roads used, the truck profile and the telemetry
  // observation. Any mismatch throws and nothing is written. A provider may
  // be consulted only before the transaction opens, never inside it.
  private async Task<IDbContextTransaction> BeginVerifiedPublicationAsync(
    RoutePlanningState state,
    FuelWorkInputs captured,
    RoutePlan plan,
    TruckRouteProfile profile,
    IReadOnlyCollection<SavedRoadVersion> savedRoads,
    IReadOnlyCollection<DeadheadHistoryBatch> history,
    CancellationToken ct
  )
  {
    var stamp = FuelObservationStamp.Capture(state);
    RequireSameTelemetry(stamp, await plans.GetAsync(captured.Root(plan), ct));
    var transaction = await publication.BeginAsync(
      captured.Itinerary,
      history,
      ct
    );
    try
    {
      await roads.RequireCurrentAsync(
        [
          .. savedRoads,
          state.SavedRoad
            ?? throw new InvalidOperationException(
              "Fuel publication requires the captured current road."
            ),
        ],
        ct
      );
      await profiles.RequireCurrentAsync(plan.TruckId, state.Profile, ct);
      RequireSameTelemetry(
        stamp,
        await plans.GetAsync(
          captured.Root(plan),
          ct,
          PlannedRouteTelemetry.WithoutProviderWait
        )
      );
      await profiles.SaveAsync(plan.TruckId, profile, ct);
      return transaction;
    }
    catch
    {
      await transaction.DisposeAsync();
      throw;
    }
  }

  private async Task<FuelPlan> CommitAsync(
    FuelPlan fuel,
    IReadOnlyList<FuelCandidate> purchases,
    TruckRoute baseline,
    IReadOnlyList<FuelItineraryStop> itinerary,
    List<Guid> ids,
    Dictionary<Guid, string> signatures,
    string assignmentSignature,
    IReadOnlyDictionary<Guid, string> assignmentSignatures,
    IReadOnlyCollection<DeadheadHistoryBatch> history,
    IReadOnlyCollection<SavedRoadVersion> savedRoads,
    FuelWorkInputs captured,
    RoutePlanningState state,
    TruckRouteProfile profile,
    bool manual,
    string priceSignature,
    DateOnly today,
    DateTime? expectedCalculatedAt,
    CancellationToken ct
  )
  {
    var plan = state.Plan!;
    fuel.TruckId = plan.TruckId;
    fuel.ExecutionLegId = plan.ExecutionLegId;
    fuel.AssignmentRevision = plan.AssignmentRevision;
    fuel.StartProgressMiles = state.Progress!.ProgressMiles!.Value;
    fuel.DispatchIds = ids;
    fuel.DispatchSignatures = signatures;
    fuel.AssignmentSignature = assignmentSignature;
    if (fuel.ArrivalPolicy?.NextDispatchId is { } nextDispatchId)
    {
      if (
        !assignmentSignatures.TryGetValue(nextDispatchId, out var nextSignature)
      )
        throw new RoutePlanningException(
          "The pickup after the fuel horizon changed during calculation."
        );
      fuel.DispatchSignatures[nextDispatchId] = nextSignature;
    }
    fuel.ProfileSignature = JsonSerializer.Serialize(
      profile,
      RoutePlanningService.Json
    );
    fuel.PricingDate = today;
    fuel.PriceSignature = priceSignature;
    fuel.ManualStartingFuel = manual;
    fuel.FuelObservedAt = state.FuelUpdatedAt;
    if (manual)
      fuel.Notes.Add("Starting fuel was entered manually.");
    fuel.Notes.Add(
      "Cost comparison excludes toll differences. Fuel readings and schedule forecasts are estimates."
    );
    double accessMiles = fuel.StartAccessMiles;
    for (var i = 0; i < fuel.Stops.Count; i++)
    {
      var candidate = purchases[i];
      var owner = itinerary[candidate.LegIndex];
      fuel.Stops[i].VisitKey = candidate.VisitKey;
      fuel.Stops[i].DispatchId = owner.DispatchId;
      fuel.Stops[i].BeforeStopId = owner.Stop.Id;
      fuel.Stops[i].CurrentRouteMile = null;
      fuel.Stops[i].CashUsdPerGallon = candidate.PriceUsd;
      fuel.Stops[i].EconomicUsdPerGallon = candidate.EconomicPriceUsd;
      fuel.Stops[i].RouteMilesAhead = candidate.AlongMiles;
      fuel.Stops[i].MilesAhead =
        candidate.AlongMiles + accessMiles + candidate.ExtraInMiles;
      accessMiles += candidate.ExtraInMiles + candidate.ExtraOutMiles;
    }
    fuel.StopArrivals = FuelStopArrivals.Calculate(fuel, itinerary, profile);
    var latest = await inputs.ReadFreshAsync(plan.TruckId, ct);
    var latestLoads = latest.Select(plan);
    if (
      latest.Itinerary.InputSignature != captured.Itinerary.InputSignature
      || !FuelPlanProjection.AssignmentsMatch(
        fuel,
        plan.DispatchId,
        latestLoads
      )
      || !FuelPlanProjection.RemainingStopsMatch(
        itinerary,
        plan.DispatchId,
        plan.Tracking.NextStopId,
        latestLoads
      )
    )
      throw new RoutePlanningException(
        "Assignments changed during fuel calculation."
      );
    await using var transaction = await BeginVerifiedPublicationAsync(
      state,
      captured,
      plan,
      profile,
      savedRoads,
      history,
      ct
    );
    SavedRoadVersion[] dependencies =
    [
      .. savedRoads,
      state.SavedRoad
        ?? throw new InvalidOperationException(
          "Fuel publication requires the captured current road."
        ),
    ];
    await routeStore.StoreFuelAsync(
      plan.DispatchId,
      fuel,
      ct,
      plan.ExecutionLegId
    );
    if (
      !await savedPlans.ReplaceAsync(
        new(
          plan.TruckId,
          plan.DispatchId,
          fuel.CalculatedAt,
          fuel,
          itinerary,
          null
        )
        {
          BaselineRoute = baseline,
          RoadDependencies = FuelRoadDependencies.Capture(dependencies),
          HistoryDependencies = FuelHistoryDependencies.Capture(history),
          RootExecutionLegId = plan.ExecutionLegId,
          AssignmentRevision = plan.AssignmentRevision,
        },
        expectedCalculatedAt,
        ct
      )
    )
      throw new PlanningSettingsConflictException(
        "The fuel plan changed in another session. Reopen it before saving."
      );
    await transaction.CommitAsync(ct);
    profiles.Invalidate(plan.TruckId);
    savedPlans.Invalidate(plan.TruckId);
    routeStore.Invalidate(plan.DispatchId, plan.ExecutionLegId);
    return fuel;
  }

  private async Task<RouteWorkSnapshot> FuelLoadAsync(
    Guid dispatchId,
    Guid? executionLegId,
    long? assignmentRevision,
    CancellationToken ct
  )
  {
    var load = await plans.LoadAsync(dispatchId, ct, executionLegId);
    if (
      executionLegId == Guid.Empty
      || assignmentRevision < 0
      || load.ExecutionLegId.HasValue
        && (
          load.ExecutionLegId != executionLegId
          || load.AssignmentRevision != assignmentRevision
          || !PlanningWorkPolicy.CanUseGps(load)
        )
      || !load.ExecutionLegId.HasValue
        && (executionLegId.HasValue || assignmentRevision is > 0)
    )
      throw new PlanningSettingsConflictException(
        "Execution changed. Reopen the current truck fuel plan."
      );
    return load;
  }

  private static bool SameVehicle(TruckRouteProfile a, TruckRouteProfile b) =>
    a.HeightFeet == b.HeightFeet
    && a.WidthFeet == b.WidthFeet
    && a.LengthFeet == b.LengthFeet
    && a.WeightPounds == b.WeightPounds
    && a.Axles == b.Axles
    && a.AxleWeightPounds == b.AxleWeightPounds
    && a.Hazmat == b.Hazmat;
}
