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
    var plan = FuelPlanningGuards.RequirePlan(state, p);
    if (plan.ExecutionLegId.HasValue)
      await RequireCurrentAsync(
        captured,
        dispatchId,
        plan.ExecutionLegId,
        plan.AssignmentRevision,
        state.Profile,
        ct
      );
    FuelPlanningGuards.RequireDrivable(state, plan, p);
    var manual = request.CurrentGallons.HasValue;
    var gallons = FuelStartingLevel.Gallons(
      request.CurrentGallons,
      state,
      p,
      DateTime.UtcNow
    );
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
    var remaining = horizon.Route.Miles;
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
    var comparison = await FuelChainComparison.RunAsync(
      chains,
      Math.Min(
        config.CandidateRoadChecks,
        horizon.StartAccessMiles > 0 ? 11 : 12
      ),
      calendar,
      schedule,
      baseline,
      geometry,
      horizon.StartAccessMiles,
      optimization,
      p,
      ct
    );
    var bestFuel = comparison.Fuel;
    var bestPurchases = comparison.Purchases;
    var checks = comparison.Checks;
    var winner = comparison.Winner;
    var evaluatedRoutes = comparison.Evaluated;
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
    FuelPlanSummary.Write(
      bestFuel,
      p,
      checks,
      winner!,
      calendar,
      horizon,
      baseline,
      evaluatedRoutes
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
}
