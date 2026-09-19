using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Models;
using System.Text.Json;
using Application.Features.Routing.Options;
using Microsoft.Extensions.Options;
using Application.Caching;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelPlanningService(RoutePlanningService plans, ISender mediator,
  Application.Features.Dispatch.Interfaces.IDispatchBoardReader dispatchBoard, FuelRegionPlanner regions, FuelHorizon horizons, IOptions<FuelRegionOptions> options,
  TruckFuelPlans savedPlans, FuelScheduleEvaluator schedules, IAppDbContext db, ProcessGates processGates)
{
  private readonly KeyedGates truckGates = processGates.For<FuelPlanningService>();
  // Manual searches share a small per-process budget; ordinary planning reads never acquire it.
  private static readonly SemaphoreSlim SearchSlots = new(2, 2);

  public async Task<FuelPlan> BuildAsync(Guid dispatchId, FuelBuildRequest request, CancellationToken ct)
  {
    var truckId = (await plans.LoadAsync(dispatchId, ct)).TruckId
      ?? throw new RoutePlanningException("A truck assignment is required for fuel planning.");
    var gate = truckGates.For(truckId);
    await GateWait.WaitAsync(gate, "FuelTruck", ct);
    try
    {
      await GateWait.WaitAsync(SearchSlots, "FuelSearch", ct);
      try { return await BuildCoreAsync(dispatchId, request, ct); }
      finally { SearchSlots.Release(); }
    }
    finally { gate.Release(); }
  }

  private async Task<FuelPlan> BuildCoreAsync(Guid dispatchId, FuelBuildRequest request, CancellationToken ct,
    bool replaceManual = false, DateTime? expectedCalculatedAt = null)
  {
    var config = options.Value;
    var state = await plans.GetAsync(dispatchId, ct);
    var existing = await savedPlans.ReadUncachedAsync(state.Plan?.TruckId
      ?? (await plans.LoadAsync(dispatchId, ct)).TruckId!.Value, ct);
    if (replaceManual) RequireRevision(existing, expectedCalculatedAt);
    else if (existing?.Plan.ManuallyEdited == true)
      throw new RoutePlanningException("This plan has manual changes. Use Edit fuel plan or explicitly reset it to automatic.");
    var p = request.Profile;
    PlanningPreferences.From(state.Profile).ApplyTo(p);
    p.UsesFleetDefaults = state.Profile.UsesFleetDefaults;
    if (p.Validate(true) is { } error) throw new RoutePlanningException(error);
    var plan = state.Plan ?? throw new RoutePlanningException("Build the truck route first.");
    if (plan.InputsChanged || !SameVehicle(p, plan.Profile)) throw new RoutePlanningException("The truck or stops changed. Rebuild the route before planning fuel.");
    if (state.Progress?.RemainingMiles is not { } remaining || state.Progress.ProgressMiles is null
      || state.Progress.Position?.IsValid != true || state.Progress.LocationStale)
      throw new RoutePlanningException("Fuel quantities will update when a fresh GPS position is available on the current route.");
    double gallons;
    var manual = request.CurrentGallons.HasValue;
    if (manual) gallons = request.CurrentGallons!.Value;
    else
    {
      if (state.FuelPercent is not { } percent || !double.IsFinite(percent) || percent is < 0 or > 100
        || state.FuelUpdatedAt is null || state.FuelUpdatedAt > DateTime.UtcNow.AddMinutes(1))
        throw new RoutePlanningException("No valid fuel level is available for this truck.");
      gallons = p.TankGallons!.Value * percent / 100;
    }
    if (FuelReservePolicy.StartingLevelError(gallons, p) is { } levelError)
      throw new RoutePlanningException(levelError);
    await plans.SaveProfileAsync(dispatchId, p, ct);
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    var response = await mediator.Send(new GetFuelStationsQuery(today), ct);
    if (!response.Success || response.Response is null) throw new RoutePlanningException("Fuel prices are temporarily unavailable.");
    var prices = FuelRegionGrid.Prices(response.Response, p, today);
    var priceSignature = FuelPriceSignature.From(prices);
    var board = await dispatchBoard.ReadAsync(new(TruckId: plan.TruckId,
      IncludeHos: false, IncludeFinancials: false, IncludeEta: false, IncludeOverdue: true), ct);
    var loads = board.Items.FirstOrDefault()?.Dispatches ?? [];
    var assignmentSignatures = loads.ToDictionary(x => x.Id, FuelHorizon.LoadSignature);
    var horizon = await horizons.BuildAsync(state, p, ct);
    var terminal = new RoutePlan { TruckId = plan.TruckId, DispatchId = horizon.DispatchIds[^1], Route = horizon.Route };
    var geometry = new FuelSearchGeometry(horizon.Route, ct);
    var arrival = await regions.BuildAsync(terminal, p, prices, 0, ct, geometry);
    remaining = horizon.Route.Miles;
    var shortList = FuelAccessEstimate.Nearby(FuelRouteOccurrences.Create(horizon.Route, prices, config, ct, geometry,
      FuelAccessEstimate.NearbyMiles));
    var selected = FuelRouteSearch.SelectCandidates(shortList, remaining, gallons, p, arrival, config,
      includeAccess: true, initialAccessMiles: horizon.StartAccessMiles);
    var stops = horizon.Stops;
    if (stops.Count == 0) throw new RoutePlanningException("No remaining dispatch stops.");
    var baseline = horizon.Route;
    var initialMinutes = FuelAccessEstimate.DrivingMinutes(horizon.StartAccessMiles);
    var scheduleState = state with { Progress = state.Progress! with { Position = baseline.Legs[0].Points[0] } };
    var schedule = await schedules.PrepareAsync(scheduleState, baseline, horizon.Itinerary, DateTime.UtcNow, ct);
    var chains = FuelZoneSearch.Schedule(FuelRouteSearch.Chains(selected, remaining, gallons, p, arrival, ct,
      includeAccess: true, initialAccessMiles: horizon.StartAccessMiles),
      FuelZoneSearch.Representatives(selected, config));
    FuelPlan? bestFuel = null;
    List<FuelCandidate> bestPurchases = [];
    (int Cycle, int Unknown, int Late) bestImpact = (int.MaxValue, int.MaxValue, int.MaxValue);
    double bestScore = double.PositiveInfinity;
    var evaluatedRoutes = 0;
    var seen = new HashSet<string>();
    var checks = new List<FuelRouteCheck>();
    FuelRouteCheck? winner = null;
    var comparisonLimit = Math.Min(config.CandidateRoadChecks, horizon.StartAccessMiles > 0 ? 11 : 12);
    foreach (var chain in chains)
    {
      if (evaluatedRoutes >= comparisonLimit) break;
      var ordered = chain.OrderBy(x => x.AlongMiles).ToList();
      if (!seen.Add(string.Join(",", ordered.Select(x => x.VisitKey)))) continue;
      var check = new FuelRouteCheck { Stations = ordered.Select(x => x.Station.Name).ToList() };
      ct.ThrowIfCancellationRequested();
      evaluatedRoutes++;
      checks.Add(check);
      var extraMiles = horizon.StartAccessMiles + ordered.Sum(x => x.ExtraInMiles + x.ExtraOutMiles);
      var extraMinutes = initialMinutes + ordered.Sum(x => x.Station.DetourMinutes);
      check.ExtraMiles = extraMiles; check.ExtraMinutes = extraMinutes;
      FuelPlan fuel;
      List<FuelCandidate> purchases;
      try { (fuel, purchases) = FuelOptimizer.OptimizeWithVisits(baseline.Miles, gallons, p, ordered, plan.Version, p.UseIfta,
        compare: false, arrivalPolicy: arrival, initialAccessMiles: horizon.StartAccessMiles); }
      catch (RoutePlanningException) { check.Result = "Cannot preserve fuel reserve"; continue; }
      // Do not keep an unnecessary waypoint when the optimizer buys nothing there.
      if (fuel.Stops.Count != ordered.Count) { check.Result = "Includes a station without a useful purchase"; continue; }
      fuel.EconomicCostUsd += initialMinutes / 60 * p.DriverHourlyCostUsd;
      if (FuelScheduleRanking.CanSkipReplay(fuel, 0, p.DriverHourlyCostUsd,
        bestFuel is null ? null : bestImpact, bestScore, bestFuel?.Stops.Count ?? 0))
      { check.Result = "Higher estimated cost before schedule replay"; continue; }
      fuel.ScheduleImpact = chain.Count == 0 && horizon.StartAccessMiles == 0 ? schedule.Baseline
        : schedule.Evaluate(FuelAccessEstimate.TimingRoute(baseline, purchases, horizon.StartAccessMiles), ct);
      var impact = FuelScheduleRanking.For(fuel.ScheduleImpact);
      // Access driving time is already priced by the optimizer; add only further schedule delay.
      var timeCost = Math.Max(0, FuelScheduleRanking.DelayCost(fuel.ScheduleImpact, extraMiles, extraMinutes, p.DriverHourlyCostUsd)
        - extraMinutes / 60 * p.DriverHourlyCostUsd);
      var score = fuel.EconomicCostUsd + fuel.ExpectedFutureFuelCostUsd + timeCost;
      check.CostUsd = score; check.Result = "Higher total cost";
      if (impact.CompareTo(bestImpact) > 0) { check.Result = "Worse schedule feasibility"; continue; }
      if (impact.CompareTo(bestImpact) == 0
        && FuelStopEconomy.Compare(score, fuel.Stops.Count, bestScore, bestFuel!.Stops.Count) >= 0)
      {
        if (score < bestScore) check.Result = "Additional stops save less than $20 each";
        continue;
      }
      fuel.ExtraMinutes = extraMinutes;
      fuel.EconomicCostUsd += timeCost;
      fuel.SavingsUsd = null;
      winner = check;
      bestScore = score; bestFuel = fuel;
      bestPurchases = purchases; bestImpact = impact;
    }
    if (bestFuel is null)
      throw new RoutePlanningException((gallons < p.ReserveGallons
        ? "No complete fuel plan was found from the reported low fuel level. Confirm the fuel level or arrange refueling before driving. "
        : "") + "No nearby station chain preserves the fuel reserve using estimated access. " + $"Horizon: {remaining:N0} mi / {horizon.DispatchIds.Count} trips; "
        + $"starting fuel: {gallons:N1} gal; arrival minimum: {arrival.MinimumGallons:N1} gal; "
        + $"working capacity: {p.TankGallons * p.FillPercent / 100:N1} gal. "
        + $"Candidates: {selected.Count}. Checks: "
        + string.Join("; ", checks.Select(c => $"{(c.Stations.Count == 0 ? "Direct" : string.Join(" + ", c.Stations))}: {c.Result}, +{c.ExtraMiles:N1} mi / +{c.ExtraMinutes:N1} min")));
    winner!.Result = "Selected";
    bestFuel.RouteChecks = checks;
    bestFuel.EstimatedStationAccess = true;
    bestFuel.StartAccessMiles = horizon.StartAccessMiles;
    bestFuel.RemainingMiles = baseline.Miles + horizon.StartAccessMiles + bestFuel.Stops.Sum(x => x.DetourMiles);
    bestFuel.Notes.Add($"Compared {evaluatedRoutes} fuel chains on the saved route without routing requests. Station access distance and time are estimates, not verified truck approaches.");
    bestFuel.Notes.AddRange(horizon.Notes);
    bestFuel.Notes.Add("Additional fuel stops must save at least $20 each against a feasible alternative with fewer stops and the same schedule rank. This is a selection threshold, not a stop charge.");
    return await CommitAsync(bestFuel, bestPurchases, baseline, horizon.Itinerary, horizon.DispatchIds,
      horizon.DispatchSignatures, horizon.AssignmentSignature, assignmentSignatures, state, p, manual, priceSignature, today,
      existing?.CalculatedAt, ct);
  }

  private async Task<FuelPlan> CommitAsync(FuelPlan fuel, IReadOnlyList<FuelCandidate> purchases,
    TruckRoute baseline, IReadOnlyList<FuelItineraryStop> itinerary, List<Guid> ids, Dictionary<Guid, string> signatures,
    string assignmentSignature, IReadOnlyDictionary<Guid, string> assignmentSignatures,
    RoutePlanningState state, TruckRouteProfile profile, bool manual,
    string priceSignature, DateOnly today, DateTime? expectedCalculatedAt, CancellationToken ct)
  {
    var plan = state.Plan!;
    fuel.TruckId = plan.TruckId; fuel.StartProgressMiles = state.Progress!.ProgressMiles!.Value;
    fuel.DispatchIds = ids; fuel.DispatchSignatures = signatures; fuel.AssignmentSignature = assignmentSignature;
    if (fuel.ArrivalPolicy?.NextDispatchId is { } nextDispatchId)
    {
      if (!assignmentSignatures.TryGetValue(nextDispatchId, out var nextSignature))
        throw new RoutePlanningException("The pickup after the fuel horizon changed during calculation.");
      fuel.DispatchSignatures[nextDispatchId] = nextSignature;
    }
    fuel.ProfileSignature = JsonSerializer.Serialize(profile, RoutePlanningService.Json);
    fuel.PricingDate = today; fuel.PriceSignature = priceSignature;
    fuel.ManualStartingFuel = manual; fuel.FuelObservedAt = state.FuelUpdatedAt;
    if (manual) fuel.Notes.Add("Starting fuel was entered manually.");
    fuel.Notes.Add("Cost comparison excludes toll differences. Fuel readings and schedule forecasts are estimates.");
    double accessMiles = fuel.StartAccessMiles;
    for (var i = 0; i < fuel.Stops.Count; i++)
    {
      var candidate = purchases[i];
      var owner = itinerary[candidate.LegIndex];
      fuel.Stops[i].VisitKey = candidate.VisitKey; fuel.Stops[i].DispatchId = owner.DispatchId;
      fuel.Stops[i].BeforeStopId = owner.Stop.Id; fuel.Stops[i].CurrentRouteMile = null;
      fuel.Stops[i].CashUsdPerGallon = candidate.PriceUsd;
      fuel.Stops[i].EconomicUsdPerGallon = candidate.EconomicPriceUsd;
      fuel.Stops[i].RouteMilesAhead = candidate.AlongMiles;
      fuel.Stops[i].MilesAhead = candidate.AlongMiles + accessMiles + candidate.ExtraInMiles;
      accessMiles += candidate.ExtraInMiles + candidate.ExtraOutMiles;
    }
    fuel.StopArrivals = FuelStopArrivals.Calculate(fuel, itinerary, profile);
    var latest = (await dispatchBoard.ReadAsync(new(TruckId: plan.TruckId, IncludeHos: false, IncludeFinancials: false, IncludeEta: false, IncludeOverdue: true), ct))
      .Items.FirstOrDefault()?.Dispatches ?? [];
    if (!FuelPlanProjection.AssignmentsMatch(fuel, plan.DispatchId, latest)
      || !FuelPlanProjection.RemainingStopsMatch(itinerary, plan.DispatchId, plan.Tracking.NextStopId, latest))
      throw new RoutePlanningException("Assignments changed during fuel calculation.");
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    await plans.StoreFuelAsync(plan.DispatchId, fuel, ct);
    if (!await savedPlans.ReplaceAsync(new(plan.TruckId, plan.DispatchId, fuel.CalculatedAt, fuel, itinerary, null)
      { BaselineRoute = baseline }, expectedCalculatedAt, ct))
      throw new PlanningSettingsConflictException("The fuel plan changed in another session. Reopen it before saving.");
    await transaction.CommitAsync(ct);
    savedPlans.Invalidate(plan.TruckId);
    plans.InvalidateReadCache(plan.DispatchId);
    return fuel;
  }

  private static bool SameVehicle(TruckRouteProfile a, TruckRouteProfile b) =>
    a.HeightFeet == b.HeightFeet && a.WidthFeet == b.WidthFeet && a.LengthFeet == b.LengthFeet
    && a.WeightPounds == b.WeightPounds && a.Axles == b.Axles && a.AxleWeightPounds == b.AxleWeightPounds && a.Hazmat == b.Hazmat;
}
