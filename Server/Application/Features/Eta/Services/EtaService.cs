using System.Text.Json;
using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Microsoft.Extensions.Options;

namespace Application.Features.Eta.Services;

public sealed class EtaService(
  IAppDbContext db,
  IDriverHosProvider hos,
  IRouteRegionLookup regions,
  EtaMemory memory,
  IHosHistoryProvider historyProvider,
  IOptions<EtaPlanningOptions> planningOptions
)
{
  private readonly EtaPlanningOptions planning = planningOptions.Value;

  private static string RouteKey(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.Id,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Version,
        state.Plan?.Tracking.NextStopId,
        state.Plan?.Tracking.PassedStopIds,
        state.Plan?.InputsChanged,
        state.Plan?.Stops,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
      }
    );

  public DispatchEta? GetCached(RoutePlanningState state)
  {
    if (state.Plan is not { } plan)
      return null;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    memory.View(key, DateTime.UtcNow);
    if (memory.Results.TryGetValue(key, out var entry))
    {
      if (
        entry.RouteKey == RouteKey(state)
        && entry.Value.ValidUntil > DateTime.UtcNow
      )
        return entry.Value;
      if (memory.RemoveIfCurrent(key, entry))
        memory.RequestRefresh();
    }
    return null;
  }

  public async Task<DispatchEta?> GetAsync(
    RoutePlanningState state,
    CancellationToken ct,
    bool viewed = true,
    EtaChainPlan? chain = null
  )
  {
    if (state.Plan is not { } plan)
      return null;
    var now = DateTime.UtcNow;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    if (viewed)
      memory.View(key, now);
    var driver = plan.ExecutionLegId is { } legId
      ? await db
        .ExecutionLegs.AsNoTracking()
        .Where(x =>
          x.Id == legId
          && x.TruckId == plan.TruckId
          && x.Revision == plan.AssignmentRevision
          && (x.Status == "active" || x.Status == "planned")
        )
        .Select(x =>
          x.DriverId.HasValue
            ? db
              .Drivers.Where(d => d.Id == x.DriverId)
              .Select(d => d.ExternalId)
              .FirstOrDefault()
            : null
        )
        .SingleOrDefaultAsync(ct) ?? ""
      : await db
        .Trucks.AsNoTracking()
        .Where(x => x.Id == plan.TruckId)
        .Select(x => x.Driver == null ? "" : x.Driver.ExternalId)
        .SingleOrDefaultAsync(ct) ?? "";
    var signature = JsonSerializer.Serialize(
      new
      {
        plan.Id,
        plan.ExecutionLegId,
        plan.AssignmentRevision,
        plan.Version,
        driver,
        plan.Stops,
        plan.Tracking.NextStopId,
        plan.Tracking.PassedStopIds,
        plan.InputsChanged,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
        Chain = chain?.InputHash,
      }
    );
    var gate = memory.Gate(key);
    await gate.WaitAsync(ct);
    try
    {
      if (
        memory.Results.TryGetValue(key, out var cached)
        && cached.Signature == signature
        && cached.Value.ValidUntil > DateTime.UtcNow
      )
        return cached.Value;
      var clocks = await hos.GetClocksAsync(ct);
      clocks.TryGetValue(driver, out var clock);
      var history = clock is null
        ? null
        : await historyProvider.GetAsync(driver, ct);
      var result = Calculate(state, clock, DateTime.UtcNow, history, chain, ct);
      if (clock is null)
        result = result with { ValidUntil = DateTime.UtcNow.AddSeconds(10) };
      ct.ThrowIfCancellationRequested();
      memory.Results[key] = new(signature, result, RouteKey(state))
      {
        ChainInputHash = chain?.InputHash,
      };
      return result;
    }
    finally
    {
      gate.Release();
    }
  }

  public DispatchEta Calculate(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    EtaChainPlan? chain = null,
    CancellationToken cancellationToken = default
  )
  {
    var baseline = CalculatePlan(
      state,
      clocks,
      now,
      history,
      chain,
      HosCycleMode.Observe,
      cancellationToken
    );
    if (
      !baseline.Stops.Any(stop =>
        stop.Hours is { CycleVerified: true, FirstCycleShortageAt: not null }
      )
    )
      return baseline;
    DispatchEta? Alternative(HosCycleMode mode)
    {
      try
      {
        return CalculatePlan(
          state,
          clocks,
          now,
          history,
          chain,
          mode,
          cancellationToken
        );
      }
      catch (CycleScenarioUnavailableException)
      {
        return null;
      }
    }
    var recap = Alternative(HosCycleMode.Recap)
      ?.Stops.ToDictionary(stop => (stop.DispatchId, stop.StopId));
    var restart = baseline.Stops.Any(stop =>
      stop.Hours?.FirstCycleShortageAt is not null && stop.Appointment >= now
    )
      ? Alternative(HosCycleMode.Restart)
        ?.Stops.ToDictionary(stop => (stop.DispatchId, stop.StopId))
      : null;
    return baseline with
    {
      Stops = baseline
        .Stops.Select(stop =>
        {
          if (
            stop.Hours
            is not { CycleVerified: true, FirstCycleShortageAt: not null } hours
          )
            return stop;
          var alternatives = new List<StopHoursAlternative>();
          void Add(
            IReadOnlyDictionary<(Guid, Guid), StopEta>? candidates,
            bool requireOnTime
          )
          {
            var candidate = candidates?.GetValueOrDefault(
              (stop.DispatchId, stop.StopId)
            );
            if (
              candidate?.Hours
                is not {
                  CycleVerified: true,
                  FirstCycleShortageAt: null
                } candidateHours
              || requireOnTime && candidate.LateMinutes != 0
            )
              return;
            alternatives.AddRange(candidateHours.Alternatives);
          }
          Add(recap, false);
          Add(restart, true);
          return stop with
          {
            Hours = hours with
            {
              Alternatives = alternatives,
              UnavailableReason =
                alternatives.Count == 0
                  ? "A verified cycle alternative is not available within this forecast horizon."
                  : null,
            },
          };
        })
        .ToArray(),
    };
  }

  public DispatchEta CalculateRoad(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    EtaChainPlan? chain = null,
    CancellationToken cancellationToken = default
  ) =>
    CalculatePlan(
      state,
      clocks,
      now,
      history,
      chain,
      HosCycleMode.Observe,
      cancellationToken
    );

  public DispatchEta CalculateRoadPreview(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history = null,
    CancellationToken cancellationToken = default
  ) =>
    CalculatePlan(
      state,
      clocks,
      now,
      history,
      null,
      HosCycleMode.Observe,
      cancellationToken,
      boundedPreview: true
    );

  private DispatchEta CalculatePlan(
    RoutePlanningState state,
    DriverHosClocks? clocks,
    DateTime now,
    HosHistory? history,
    EtaChainPlan? chain,
    HosCycleMode cycleMode,
    CancellationToken cancellationToken,
    bool boundedPreview = false
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    var currentRegion =
      state.Progress is { LocationStale: false, Position: { } currentPosition }
      && double.IsFinite(currentPosition.Latitude)
      && currentPosition.Latitude is >= -90 and <= 90
      && double.IsFinite(currentPosition.Longitude)
      && currentPosition.Longitude is >= -180 and <= 180
        ? regions.Find(currentPosition)
        : null;
    var dutyStatus = HosDutyStatus.Read(
      history,
      clocks,
      now,
      currentRegion is { NorthOf60: false } ? currentRegion.Country : null
    );
    DispatchEta Missing(string reason, bool routeUpdatePending = false) =>
      new(now, now.AddMinutes(2), [], reason, [])
      {
        DutyStatus = dutyStatus,
        RouteUpdatePending = routeUpdatePending,
      };
    var plan = state.Plan;
    if (plan is null)
      return Missing("ETA unavailable: waiting for a current route.");
    if (chain?.CurrentUnavailableReason is { } assignmentReason)
      return Missing(assignmentReason);
    if (plan.InputsChanged)
      return Missing("ETA unavailable: route update in progress.", true);
    if (
      state.Progress?.LocationStale != false
      || state.Progress.ProgressMiles is not { } progress
      || !double.IsFinite(progress)
      || progress < 0
    )
      return Missing("ETA unavailable: waiting for current GPS.");
    if (
      state.Progress.OffRoute
      && (
        !double.IsFinite(state.Progress.DistanceFromRouteMiles)
        || state.Progress.DistanceFromRouteMiles
          > planning.OffRouteEstimateMaxMiles
      )
    )
      return Missing(
        "ETA unavailable: off-route distance is too large; route update in progress.",
        true
      );
    if (
      clocks?.DriveMs is null
      || clocks.ShiftMs is null
      || clocks.CycleMs is null
      || clocks.BreakMs is null
      || clocks.UpdatedAt < now.AddMinutes(-3)
    )
      return Missing("ETA unavailable: waiting for driver HOS.");
    if (plan.Route.Legs.Count == 0)
      return Missing("ETA unavailable: no route legs.");
    var timing = boundedPreview
      ? EtaRouteTiming.CompilePreview(plan.Route, regions, cancellationToken)
      : memory.Timing.GetOrCreate(plan.Id, plan.Version, plan.Route, regions);
    if (timing is null)
      return Missing(
        "Schedule preview regional timing exceeds its bounds or needs verification."
      );
    if (!timing.HasCompleteTravelTimes)
      return Missing("ETA unavailable: incomplete road travel times.");
    var initial =
      currentRegion
      ?? regions.Find(state.Progress.Position ?? plan.Route.Legs[0].Points[0]);
    if (initial.Country == "" || initial.NorthOf60)
      return Missing(
        "ETA unavailable: this regional ruleset needs verification."
      );
    // The fuel allowance is owed once per stop the plan makes to fuel, so a
    // run with a fuel plan and no pump ahead of the truck - the last miles
    // into a delivery - is not charged for one. No fuel plan is not the same
    // answer: nothing has been decided yet, and the shift keeps its
    // allowance rather than having one quietly taken away.
    var fuelStopsAhead = plan.FuelPlan?.Stops.Count(stop => stop.MilesAhead > 0);
    var clock = new HosTravelClock(
      now,
      clocks,
      initial.Country,
      history,
      planning,
      cycleMode,
      fuelStopsAhead
    );
    var cycleAtCalculation = clock.SnapshotCycle();
    try
    {
      clock.CompleteOngoingDailyRest(dutyStatus);
      if (state.Progress.OffRoute)
        clock.Drive(
          planning.TravelHours(
            state.Progress.DistanceFromRouteMiles
              * planning.OffRouteDistanceFactor,
            0
          ),
          initial.Country
        );
    }
    catch (CycleScenarioUnavailableException error)
    {
      return Missing(error.Message);
    }
    var results = new List<StopEta>();
    var assumptions = new List<string>
    {
      "Estimated using saved truck travel time; future traffic and border delays may differ.",
      clock.HistoryAvailable
        ? "Verified HOS history: eligible split rest and ongoing rest are considered; no exemption assumptions."
        : "HOS history incomplete: conservative full rests; no split credit.",
      clock.RecapVerified
        ? "Cycle starts from current ELD hours; recap uses reconciled home-day duty history."
      : clock.CycleFeasibility.Verified
        ? "Cycle starts from current ELD hours; unreconciled history does not add recap credits."
      : "Cycle history or the current ELD cycle is unavailable; cycle feasibility is unknown.",
      "Road ETA includes daily HOS and stop service but does not assume a cycle wait or restart. Cycle alternatives are conditional plans, not driver instructions.",
      "Equipment-operation waits conservatively consume duty time without rest credit; cargo service durations do not apply to equipment collection.",
      $"Planning: {planning.DrivingHoursPerShift}h driving per shift, {planning.PreTripMinutes}m PTI, one {planning.FuelStopMinutes}m fuel allowance per shift and a separate {planning.DailyBreakMinutes}m daily break. ELD limits can require stopping earlier.",
      $"Road travel times retain routing speed/traffic assumptions; planning speed is capped at {planning.PlanningSpeedCapMph} mph with {planning.TravelTimeBufferPercent}% extra travel-time allowance, not a live traffic prediction.",
      $"{planning.PickupMinutes} minutes at pickups and {planning.DeliveryMinutes} minutes at deliveries. Facility service and appointment waiting are planned sleeper time, not cycle duty or observed ELD status.",
      "Configured cycles are used when available. Missing history keeps conservative rest assumptions; cross-border history credits require verification.",
    };
    var pending = new Dictionary<Guid, string>();
    var visited = new HashSet<Guid>();
    string? blocked = null;
    bool Attempt(Action action)
    {
      if (blocked is not null)
        return false;
      try
      {
        action();
        return true;
      }
      catch (CycleScenarioUnavailableException error)
      {
        blocked = error.Message;
        return false;
      }
    }
    bool Travel(EtaRouteTimingLeg leg, double remainingFrom)
    {
      if (leg.Miles == 0 && leg.Seconds == 0)
        return true;
      var hoursPerMile =
        planning.TravelHours(leg.Miles, leg.Seconds) / leg.Miles;
      foreach (var segment in leg.Segments)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (segment.EndMiles <= remainingFrom)
          continue;
        if (!segment.IsSupported)
          return false;
        var cursor = Math.Max(segment.StartMiles, remainingFrom);
        var hours = (segment.EndMiles - cursor) * hoursPerMile;
        if (!double.IsFinite(hours) || hours > 720)
          return false;
        if (!Attempt(() => clock.Drive(hours, segment.Country)))
          return false;
      }
      return true;
    }
    void Visit(Guid dispatchId, PlanStop stop)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!visited.Add(stop.Id))
        return;
      var activity =
        dispatchId == plan.DispatchId
          ? chain?.CurrentActivities.GetValueOrDefault(stop.Id)
          : null;
      if (activity?.Completed == true)
        return;
      var timezone = string.IsNullOrWhiteSpace(stop.AppointmentTimeZoneId)
        ? regions.Find(stop.Point).TimeZoneId
        : stop.AppointmentTimeZoneId;
      var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
      var endDate = stop.ScheduledDate2 ?? stop.ScheduledDate;
      if (
        stop.ScheduledDate2 is null
        && stop.ScheduledTime2 is { } endTime
        && stop.ScheduledTime is { } startTime
        && endTime < startTime
      )
        endDate = endDate?.AddDays(1);
      var due = Appointment(
        endDate,
        stop.ScheduledTime2 ?? stop.ScheduledTime,
        zone
      );
      var arrivalAt = clock.Now;
      var atFacility =
        activity?.ArrivedAt is { } arrived
        && arrived <= now
        && state.Progress.Position is { } position
        && RouteGeometry.Distance(position, stop.Point) <= .5;
      if (atFacility)
        arrivalAt = new DateTimeOffset(
          DateTime.SpecifyKind(activity!.ArrivedAt!.Value, DateTimeKind.Utc)
        );
      var arrival = TimeZoneInfo.ConvertTime(arrivalAt, zone);
      var drive = (int)Math.Ceiling(clock.DriveHours * 60);
      var rest = (int)Math.Ceiling(clock.RestHours * 60);
      var preTrip = (int)Math.Ceiling(clock.PreTripHours * 60);
      var fuel = (int)Math.Ceiling(clock.FuelHours * 60);
      // Live clocks cannot reconstruct the balance at an already observed
      // arrival.
      var cycleAtArrival = atFacility
        ? null
        : clock.CycleFeasibility.BalanceMinutes(clock.Now);
      var currentCycle = atFacility
        ? clock.CycleFeasibility.BalanceMinutes(clock.Now)
        : null;
      var earliest = Appointment(stop.ScheduledDate, stop.ScheduledTime, zone);
      var serviceStart =
        earliest.HasValue && earliest.Value > arrivalAt
          ? earliest.Value
          : arrivalAt;
      StopServicePolicy.WaitUntil(clock, serviceStart, stop.Job);
      if (!atFacility)
        serviceStart = clock.Now;
      var minutes = StopServicePolicy.Minutes(
        stop.Job,
        planning.PickupMinutes,
        planning.DeliveryMinutes
      );
      var remaining = atFacility
        ? Math.Max(0, (serviceStart.AddMinutes(minutes) - clock.Now).TotalHours)
        : minutes / 60d;
      clock.StopRest(remaining);
      var lateMinutes = due is null
        ? (int?)null
        : (int)Math.Max(0, Math.Ceiling((arrivalAt - due.Value).TotalMinutes));
      var departure = TimeZoneInfo.ConvertTime(clock.Now, zone);
      var feasibility = clock.CycleFeasibility;
      var cycleAfterStop = feasibility.BalanceMinutes(clock.Now);
      IReadOnlyList<StopHoursAlternative> alternative =
        cycleMode != HosCycleMode.Observe
        && feasibility.Verified
        && cycleAfterStop is { } cycle
        && clock.CycleResumeAt is { } resume
          ?
          [
            new(
              cycleMode == HosCycleMode.Recap ? "recap" : "restart",
              arrival,
              departure,
              lateMinutes,
              cycle,
              clock.CycleRestStartedAt is { } restStarted
                ? TimeZoneInfo.ConvertTime(restStarted, zone)
                : null,
              TimeZoneInfo.ConvertTime(resume, zone)
            ),
          ]
          : [];
      results.Add(
        new(stop.Id, arrival, timezone, due, lateMinutes, drive, rest)
        {
          DispatchId = dispatchId,
          ServiceStart = TimeZoneInfo.ConvertTime(serviceStart, zone),
          Departure = departure,
          CycleAfterDeparture = clock.SnapshotCycle(),
          Hours = new(
            cycleAtArrival,
            cycleAfterStop,
            feasibility.DrivingShortfallMinutes,
            feasibility.FirstShortageAt,
            feasibility.Verified,
            alternative,
            feasibility.UnavailableReason,
            currentCycle
          ),
          PreTripMinutes = preTrip,
          FuelMinutes = fuel,
        }
      );
    }
    var activeFacility = plan.Stops.FirstOrDefault(stop =>
      chain?.CurrentActivities.GetValueOrDefault(stop.Id)
        is { ArrivedAt: { } at, Completed: false }
      && at <= now
      && state.Progress.Position is { } position
      && RouteGeometry.Distance(position, stop.Point) <= .5
    );
    if (activeFacility is not null)
      Attempt(() => Visit(plan.DispatchId, activeFacility));
    if (
      !plan.FromCurrentPosition
      && progress <= .5
      && plan.Stops.FirstOrDefault() is { } origin
      && !plan.Tracking.PassedStopIds.Contains(origin.Id)
    )
      Attempt(() => Visit(plan.DispatchId, origin));
    for (var i = 0; i < timing.Legs.Length && blocked is null; i++)
    {
      var leg = timing.Legs[i];
      if (leg.EndMiles < progress)
        continue;
      if (!Travel(leg, progress))
      {
        blocked ??=
          "ETA unavailable: this regional ruleset or travel horizon needs verification.";
        break;
      }
      var stopIndex = i + (plan.FromCurrentPosition ? 0 : 1);
      if (stopIndex >= plan.Stops.Count)
        continue;
      var stop = plan.Stops[stopIndex];
      if (plan.Tracking.PassedStopIds.Contains(stop.Id))
        continue;
      Attempt(() => Visit(plan.DispatchId, stop));
    }
    if (blocked is not null)
      pending[plan.DispatchId] = blocked;
    foreach (var next in chain?.Future ?? [])
    {
      blocked ??= next.UnavailableReason;
      if (blocked is null && next.Connection is { } connection)
        foreach (var leg in connection.Legs)
          if (!Travel(leg, 0))
          {
            blocked ??=
              "ETA unavailable: the preceding connection needs regional verification.";
            break;
          }
      if (
        blocked is null
        && (
          next.Route is null || next.Stops.Count != next.Route.Legs.Length + 1
        )
      )
        blocked =
          "ETA unavailable: the saved route does not match this load's stops.";
      if (blocked is null)
      {
        Attempt(() => Visit(next.DispatchId, next.Stops[0]));
        for (var i = 0; i < next.Route!.Legs.Length && blocked is null; i++)
        {
          if (!Travel(next.Route.Legs[i], 0))
          {
            blocked ??=
              "ETA unavailable: the saved route needs regional verification.";
            break;
          }
          Attempt(() => Visit(next.DispatchId, next.Stops[i + 1]));
        }
      }
      if (blocked is not null)
        pending[next.DispatchId] = blocked;
    }
    if (clock.SplitRests > 0)
      assumptions.Add($"{clock.SplitRests} split rest(s) included.");
    if (clock.RecapWaits > 0)
      assumptions.Add(
        $"{clock.RecapWaits} wait(s) for returning cycle hours included."
      );
    if (clock.PlannedOffDutyWaitHours > 0)
      assumptions.Add(
        "Appointment waits are planned sleeper periods. Qualifying full daily rest is credited once; cycle rest remains a conditional alternative, not observed driver status."
      );
    if (clock.CompletedOngoingRest)
      assumptions.Add(
        "ETA assumes the current rest of at least 3 hours continues to a full 10-hour rest; cycle limits remain separate."
      );
    if (state.Progress.OffRoute)
      assumptions.Add(
        "Off-route ETA includes a conservative return to the saved route while route refresh continues."
      );
    return new(
      now,
      now.AddMinutes(2),
      results,
      results.Count == 0 ? blocked : null,
      assumptions
    )
    {
      DutyStatus = dutyStatus,
      CycleAtCalculation = cycleAtCalculation,
      PendingDispatches = pending,
    };
  }

  public static DateTimeOffset? Appointment(
    DateOnly? date,
    TimeOnly? time,
    TimeZoneInfo zone
  )
  {
    if (date is null || time is null)
      return null;
    var local = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
    if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
      return null;
    return new DateTimeOffset(local, zone.GetUtcOffset(local));
  }
}
