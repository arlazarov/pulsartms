using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelPlanningService
{
  public async Task<FuelPlanEditPreview> EditAsync(
    Guid dispatchId,
    FuelPlanEditRequest request,
    bool save,
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
      await GateWait.WaitAsync(SearchSlots, "FuelEdit", ct);
      try
      {
        return await EditCoreAsync(dispatchId, request, save, ct);
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

  private async Task<FuelPlanEditPreview> EditCoreAsync(
    Guid dispatchId,
    FuelPlanEditRequest request,
    bool save,
    CancellationToken ct
  )
  {
    // "core-total" contains every fuel-edit stage recorded below it, so it is
    // the number to compare a request against - not the sum of the others.
    //
    // Subtracting the stages from it to find unmeasured time is only sound
    // for ONE pass whose stages did not overlap: take an interval where
    // core-total's count is 1 and read the stages of that same pass. These
    // totals accumulate per process, so subtracting across several passes, or
    // across an interval that also carried background work, produces a
    // remainder that means nothing.
    //
    // It also bounds the handler from the inside: EditFuelPlanCommand's
    // duration includes the pipeline, the gates and the response around this
    // method, and core-total is how much of it was actually spent in here.
    using var core = PerformanceStages.Start("fuel-edit", "core-total");
    var at = Stopwatch.GetTimestamp();
    var assignedLoad = await FuelLoadAsync(
      dispatchId,
      request.ExecutionLegId,
      request.AssignmentRevision,
      ct
    );
    at = Mark("load", at);
    var captured = await inputs.ReadFreshAsync(assignedLoad.TruckId!.Value, ct);
    at = Mark("inputs", at);
    assignedLoad = captured.Root(
      new()
      {
        TruckId = assignedLoad.TruckId.Value,
        DispatchId = assignedLoad.Id,
        ExecutionLegId = assignedLoad.ExecutionLegId,
        AssignmentRevision = assignedLoad.AssignmentRevision,
      }
    );
    var state = await plans.GetAsync(
      assignedLoad,
      ct,
      PlannedRouteTelemetry.Cached
    );
    at = Mark("plan", at);
    var plan =
      state.Plan
      ?? throw new RoutePlanningException(
        "A saved truck route is required before editing fuel."
      );
    await RequireCurrentAsync(
      captured,
      dispatchId,
      plan.ExecutionLegId,
      plan.AssignmentRevision,
      state.Profile,
      ct
    );
    at = Mark("current", at);
    var profile = state.Profile;
    if (profile.Validate(true) is { } profileError)
      throw new RoutePlanningException(profileError);
    if (
      plan.InputsChanged
      || plan.Tracking.AllStopsPassed
      || !FuelPlanningGuards.SameVehicle(profile, plan.Profile)
    )
      throw new RoutePlanningException(
        "The current route changed. Reopen the fuel plan after the route is ready."
      );
    if (
      state.Progress
      is not {
        LocationStale: false,
        Position.IsValid: true,
        ProgressMiles: not null
      }
    )
      throw new RoutePlanningException(
        "A current GPS position is required to preview fuel quantities."
      );
    if (
      state.FuelPercent is not { } percent
      || !double.IsFinite(percent)
      || percent is < 0 or > 100
      || state.FuelUpdatedAt is null
      || state.FuelUpdatedAt > DateTime.UtcNow.AddMinutes(1)
    )
      throw new RoutePlanningException(
        "A valid reported fuel level is required to preview this plan."
      );
    var gallons = profile.TankGallons!.Value * percent / 100;
    var saved = await savedPlans.ReadCheckedAsync(plan.TruckId, ct);
    at = Mark("saved-fuel", at);
    var editable =
      saved is not null && FuelPlanProjection.SameScope(saved, plan)
        ? saved
        : null;
    if (request.Stops is not null || save)
      RequireRevision(saved, request.ExpectedCalculatedAt);
    var loads = captured.Select(plan);
    var horizon = await horizons.BuildAsync(state, profile, ct, captured);
    at = Mark("horizon", at);
    var segments = horizon
      .Itinerary.Select(
        (visit, index) =>
          new FuelPlanEditSegment(
            visit.Stop.Id,
            index == 0 ? null : horizon.Itinerary[index - 1].Stop,
            visit.Stop,
            visit.DispatchId
          )
      )
      .ToList();
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    var response =
      await fuelPrices.ReadAsync(today, ct)
      ?? throw new RoutePlanningException(
        "Fuel prices are temporarily unavailable."
      );
    at = Mark("prices", at);
    var prices = FuelRegionGrid.Prices(response, profile, today);
    at = Mark("price-grid", at);
    // Synchronous, and over the whole horizon road: measured because nothing
    // about it being CPU makes it cheap.
    var geometry = new FuelSearchGeometry(horizon.Route, ct);
    at = Mark("geometry", at);
    var edits =
      request.Stops ?? FuelPlanEdits.Initial(editable, state, horizon);
    FuelPlanEdits.Validate(edits, editable);
    // Headroom is preview output, never an authority supplied by the request.
    edits = edits
      .Select(edit => edit with { PurchaseLimitGallons = null })
      .ToList();
    var stationNames = (editable?.Plan.Stops ?? [])
      .GroupBy(x => x.StationId)
      .ToDictionary(x => x.Key, x => x.First().Name);
    foreach (
      var station in response.Where(x => !string.IsNullOrWhiteSpace(x.Name))
    )
      stationNames[station.Id] = station.Name;
    List<FuelCandidate> candidates;
    FuelArrivalInputs arrivalInputs;
    try
    {
      candidates = FuelManualOccurrences.Resolve(
        horizon,
        prices,
        edits,
        geometry,
        ct,
        stationNames
      );
      at = Mark("occurrences", at);
      var countries = new FuelAccessCountries(regionLookup);
      if (
        candidates.Any(candidate =>
          !countries.Matches(
            geometry.At(candidate.AlongMiles, ct),
            candidate.Station
          )
        )
      )
        throw new RoutePlanningException(
          "A selected fuel stop would require an unplanned border crossing "
            + "or its country cannot be confirmed. Choose a station on the "
            + "same side of the border as this route section."
        );
      at = Mark("border-check", at);
      arrivalInputs = await regions.BuildAsync(
        new RoutePlan
        {
          TruckId = plan.TruckId,
          DispatchId = horizon.DispatchIds[^1],
          ExecutionLegId = horizon.Itinerary[^1].ExecutionLegId,
          AssignmentRevision = horizon.Itinerary[^1].AssignmentRevision,
          Route = horizon.Route,
        },
        profile,
        prices,
        0,
        ct,
        geometry,
        captured
      );
      at = Mark("arrival-regions", at);
    }
    catch (RoutePlanningException error) when (!save)
    {
      var unresolved = FuelPlanEdits.Unresolved(
        plan,
        horizon,
        edits,
        editable,
        response,
        request.Stops is not null,
        error.Message
      );
      return new(
        unresolved,
        edits,
        saved?.CalculatedAt,
        profile.TankGallons.Value,
        profile.TankGallons.Value * profile.FillPercent / 100,
        [error.Message],
        false,
        segments
      );
    }
    var arrival = arrivalInputs.Policy;
    var replay = FuelManualReplay.Evaluate(
      horizon.Route.Miles,
      gallons,
      profile,
      candidates,
      edits,
      arrival,
      plan.Version,
      horizon.StartAccessMiles
    );
    at = Mark("replay", at);
    var fuel = replay.Plan;
    fuel.TruckId = plan.TruckId;
    fuel.ExecutionLegId = plan.ExecutionLegId;
    fuel.AssignmentRevision = plan.AssignmentRevision;
    fuel.ManuallyEdited =
      request.Stops is not null || editable?.Plan.ManuallyEdited == true;
    fuel.DispatchIds = horizon.DispatchIds;
    var canonical = edits
      .Select(
        (edit, index) =>
          edit with
          {
            BeforeStopId = candidates[index].Station.BeforeStopId,
            PurchaseLimitGallons =
              replay.PurchaseLimitsGallons.ElementAtOrDefault(index),
          }
      )
      .ToList();
    if (save)
    {
      if (request.Stops is null)
        throw new RoutePlanningException(
          "Choose the fuel stops before saving."
        );
      if (replay.Errors.Count > 0)
        throw new RoutePlanningException(string.Join(" ", replay.Errors));
      var schedule = await schedules.PrepareAsync(
        state with
        {
          Progress = state.Progress! with
          {
            Position = horizon.Route.Legs[0].Points[0],
          },
        },
        horizon.Route,
        horizon.Itinerary,
        DateTime.UtcNow,
        ct
      );
      fuel.ScheduleImpact = schedule.Evaluate(
        FuelAccessEstimate.TimingRoute(
          horizon.Route,
          candidates,
          horizon.StartAccessMiles
        ),
        ct
      );
      FuelScheduleRanking.ChargeDelay(
        fuel,
        horizon.StartAccessMiles
          + candidates.Sum(x => x.ExtraInMiles + x.ExtraOutMiles),
        profile.DriverHourlyCostUsd
      );
      var signatures = loads.ToDictionary(
        x => x.Id,
        FuelWorkSignature.LoadSignature
      );
      fuel = await CommitAsync(
        fuel,
        candidates,
        horizon.Route,
        horizon.Itinerary,
        horizon.DispatchIds,
        horizon.DispatchSignatures,
        horizon.AssignmentSignature,
        signatures,
        arrivalInputs.History is { } history
          ? horizon.History.Add(history)
          : horizon.History,
        arrivalInputs.Road is { } road
          ? horizon.Roads.Add(road)
          : horizon.Roads,
        captured,
        state,
        profile,
        false,
        FuelPriceSignature.From(prices),
        today,
        request.ExpectedCalculatedAt,
        ct
      );
    }
    FuelQuantityChoices? choices = null;
    if (!save && request.QuantityStopIndex is { } selected)
    {
      using var timing = PerformanceStages.Start(
        "fuel-edit",
        "quantity-options"
      );
      choices = FuelQuantityRedistribution.Prepare(
        selected,
        horizon.Route.Miles,
        gallons,
        profile,
        candidates,
        canonical,
        arrival,
        plan.Version,
        horizon.StartAccessMiles,
        replay,
        ct
      );
    }
    return new(
      fuel,
      canonical,
      save ? fuel.CalculatedAt : saved?.CalculatedAt,
      profile.TankGallons.Value,
      profile.TankGallons.Value * profile.FillPercent / 100,
      replay.Errors,
      Segments: segments,
      QuantityChoices: choices
    );
  }

  // Close one stage and open the next. A stage that never runs - the request
  // threw before it, or its branch was not taken - records nothing, so a
  // missing row means "did not happen", not "took no time".
  private static long Mark(string stage, long since)
  {
    PerformanceStages.Elapsed("fuel-edit", stage, since);
    return Stopwatch.GetTimestamp();
  }
}
