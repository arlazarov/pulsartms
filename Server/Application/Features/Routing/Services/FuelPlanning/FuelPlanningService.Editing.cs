using Application.Diagnostics;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;

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

  public async Task<FuelCalculationResult> ResetAsync(
    Guid dispatchId,
    DateTime? expectedCalculatedAt,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var assignedLoad = await FuelLoadAsync(
      dispatchId,
      executionLegId,
      assignmentRevision,
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
        await RequireCurrentAsync(
          dispatchId,
          truckId,
          ct,
          executionLegId,
          assignmentRevision
        );
        var state = await plans.GetAsync(assignedLoad, ct);
        return await BuildCoreAsync(
          dispatchId,
          new(state.Profile)
          {
            ExecutionLegId = executionLegId,
            AssignmentRevision = assignmentRevision,
          },
          ct,
          true,
          expectedCalculatedAt
        );
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
    var state = await plans.GetAsync(
      assignedLoad,
      ct,
      PlannedRouteTelemetry.Cached
    );
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
    var profile = state.Profile;
    if (profile.Validate(true) is { } profileError)
      throw new RoutePlanningException(profileError);
    if (
      plan.InputsChanged
      || plan.Tracking.AllStopsPassed
      || !SameVehicle(profile, plan.Profile)
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
    var editable =
      saved is not null && FuelPlanProjection.SameScope(saved, plan)
        ? saved
        : null;
    if (request.Stops is not null || save)
      RequireRevision(saved, request.ExpectedCalculatedAt);
    var loads = captured.Select(plan);
    var horizon = await horizons.BuildAsync(state, profile, ct, captured);
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
    var response = await mediator.Send(new GetFuelStationsQuery(today), ct);
    if (!response.Success || response.Response is null)
      throw new RoutePlanningException(
        "Fuel prices are temporarily unavailable."
      );
    var prices = FuelRegionGrid.Prices(response.Response, profile, today);
    var geometry = new FuelSearchGeometry(horizon.Route, ct);
    var edits = request.Stops ?? InitialEdits(editable, state, horizon);
    ValidateQuantities(edits, editable);
    // Headroom is preview output, never an authority supplied by the request.
    edits = edits
      .Select(edit => edit with { PurchaseLimitGallons = null })
      .ToList();
    var stationNames = (editable?.Plan.Stops ?? [])
      .GroupBy(x => x.StationId)
      .ToDictionary(x => x.Key, x => x.First().Name);
    foreach (
      var station in response.Response.Where(x =>
        !string.IsNullOrWhiteSpace(x.Name)
      )
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
    }
    catch (RoutePlanningException error) when (!save)
    {
      var unresolved = new FuelPlan
      {
        TruckId = plan.TruckId,
        ExecutionLegId = plan.ExecutionLegId,
        AssignmentRevision = plan.AssignmentRevision,
        DispatchIds = horizon.DispatchIds,
        ManuallyEdited =
          request.Stops is not null || editable?.Plan.ManuallyEdited == true,
        NeedsRefresh = true,
        RefreshReasons = [error.Message],
      };
      unresolved.Stops = edits
        .Select(
          (edit, index) =>
          {
            var previous = editable?.Plan.Stops.FirstOrDefault(x =>
              x.StationId == edit.StationId
              && (
                !edit.BeforeStopId.HasValue
                || x.BeforeStopId == edit.BeforeStopId
              )
            );
            var station = response.Response.FirstOrDefault(x =>
              x.Id == edit.StationId
            );
            return new FuelPlanStop
            {
              Number = index + 1,
              StationId = edit.StationId,
              BeforeStopId = edit.BeforeStopId ?? Guid.Empty,
              DispatchId =
                horizon
                  .Itinerary.FirstOrDefault(x => x.Stop.Id == edit.BeforeStopId)
                  ?.DispatchId
                ?? previous?.DispatchId
                ?? Guid.Empty,
              VisitKey = previous?.VisitKey ?? "",
              Point = previous?.Point ?? new(0, 0),
              Name = station?.Name ?? previous?.Name ?? "Unavailable station",
              Address = station?.Address ?? previous?.Address ?? "",
              BuyGallons = edit.BuyGallons,
              FillToTarget = edit.FillToTarget,
            };
          }
        )
        .ToList();
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
      var extraMiles =
        horizon.StartAccessMiles
        + candidates.Sum(x => x.ExtraInMiles + x.ExtraOutMiles);
      fuel.EconomicCostUsd += Math.Max(
        0,
        FuelScheduleRanking.DelayCost(
          fuel.ScheduleImpact,
          extraMiles,
          fuel.ExtraMinutes,
          profile.DriverHourlyCostUsd
        )
          - fuel.ExtraMinutes / 60 * profile.DriverHourlyCostUsd
      );
      var signatures = loads.ToDictionary(x => x.Id, FuelHorizon.LoadSignature);
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

  private async Task RequireCurrentAsync(
    Guid dispatchId,
    Guid truckId,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var captured = await inputs.ReadFreshAsync(truckId, ct);
    await RequireCurrentAsync(
      captured,
      dispatchId,
      executionLegId,
      assignmentRevision,
      await plans.ProfileAsync(truckId, ct),
      ct
    );
  }

  private async Task RequireCurrentAsync(
    FuelWorkInputs captured,
    Guid dispatchId,
    Guid? executionLegId,
    long? assignmentRevision,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var segment = captured.Itinerary.Segments.SingleOrDefault(x =>
      x.Work.DispatchId == dispatchId && x.Work.ExecutionLegId == executionLegId
    );
    var current = segment is null
      ? null
      : PlanningWorkPolicy.Resolve(captured.Itinerary, segment);
    if (
      current is null
      || executionLegId.HasValue
        && (
          current.AssignmentRevision != assignmentRevision
          || current.Stops.FirstOrDefault()?.AwaitingHandoff == true
        )
      || !await PlanningWorkPolicy.IsCurrentAsync(
        captured.Itinerary,
        current,
        routeStore,
        profile,
        ct
      )
    )
      throw new RoutePlanningException(
        "The truck's current load changed. Reopen its fuel plan."
      );
  }

  private static void RequireRevision(
    TruckFuelPlanSnapshot? saved,
    DateTime? expected
  )
  {
    if (
      expected is { } revision
        && (revision.Kind != DateTimeKind.Utc || revision == default)
      || (saved?.CalculatedAt.Ticks / 10) != (expected?.Ticks / 10)
    )
      throw new PlanningSettingsConflictException(
        "The fuel plan changed in another session. Reopen it before saving."
      );
  }

  private static List<FuelPlanEditStop> InitialEdits(
    TruckFuelPlanSnapshot? saved,
    RoutePlanningState state,
    FuelHorizonResult horizon
  )
  {
    if (
      saved is null
      || state.Plan is null
      || !FuelPlanProjection.SameScope(saved, state.Plan)
    )
      return [];
    var firstStop = horizon.Itinerary[0].Stop.Id;
    var index = saved
      .Stops.ToList()
      .FindIndex(x =>
        x.Stop.Id == firstStop && x.DispatchId == state.Plan!.DispatchId
      );
    var along = double.NegativeInfinity;
    if (
      saved.Plan.EstimatedStationAccess
      && index >= 0
      && saved.BaselineRoute?.Legs.ElementAtOrDefault(index) is { } leg
      && saved.Plan.DispatchSignatures.GetValueOrDefault(state.Plan!.DispatchId)
        == horizon.DispatchSignatures.GetValueOrDefault(state.Plan.DispatchId)
    )
    {
      var tail = RemainingFuelRoute.TryRead(
        new RoutePlan
        {
          FromCurrentPosition = true,
          Stops = [saved.Stops[index].Stop],
          Route = new() { Legs = [leg] },
        },
        [horizon.Itinerary[0].Stop],
        state.Progress!.Position!,
        FuelAccessEstimate.CurrentPositionToleranceMiles
      );
      var current = horizon.Route.Legs[0];
      // Only verified identical remaining road geometry can silently discard
      // passed purchases.
      if (
        tail?.Legs[0] is { } remainingLeg
        && Math.Abs(remainingLeg.Miles - current.Miles) < .01
        && remainingLeg.Points.Count == current.Points.Count
        && remainingLeg
          .Points.Zip(current.Points)
          .All(x => RouteGeometry.Distance(x.First, x.Second) < .00001)
      )
        along =
          (index == 0 ? 0 : saved.Stops[index - 1].EndMiles)
          + leg.Miles
          - remainingLeg.Miles;
    }
    var remaining = horizon.Itinerary.Select(x => x.Stop.Id).ToHashSet();
    return saved
      .Plan.Stops.Where(x =>
        remaining.Contains(x.BeforeStopId)
        && FuelReservePolicy.PurchaseNotPassed(
          saved.Plan.EstimatedStationAccess
            ? x.RouteMilesAhead ?? x.MilesAhead
            : x.MilesAhead,
          along
        )
      )
      .Select(x => new FuelPlanEditStop(
        x.StationId,
        x.BeforeStopId,
        x.BuyGallons,
        x.FillToTarget
      ))
      .ToList();
  }

  private static void ValidateQuantities(
    IReadOnlyList<FuelPlanEditStop> edits,
    TruckFuelPlanSnapshot? saved
  )
  {
    if (edits.Count > 40)
      throw new RoutePlanningException(
        "A fuel plan can contain at most 40 stops."
      );
    foreach (var edit in edits)
    {
      if (
        edit is null
        || edit.StationId == Guid.Empty
        || !double.IsFinite(edit.BuyGallons)
        || edit.BuyGallons is < 0 or > 500
      )
        throw new RoutePlanningException(
          "Choose a valid station and fuel quantity."
        );
      if (edit.FillToTarget)
        continue;
      var unchanged =
        saved?.Plan.Stops.Any(x =>
          x.StationId == edit.StationId
          && (
            !edit.BeforeStopId.HasValue || x.BeforeStopId == edit.BeforeStopId
          )
          && !x.FillToTarget
          && Math.Abs(x.BuyGallons - edit.BuyGallons) < 1e-6
        ) == true;
      if (
        !unchanged
        && edit.BuyGallons < FuelOptimizer.MinimumAutomaticPurchaseGallons
      )
        throw new RoutePlanningException(
          "Choose at least 25 US gallons, or select Full tank."
        );
    }
  }
}
