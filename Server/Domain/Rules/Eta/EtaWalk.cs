using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Ports;
using Domain.Rules.Routing;

namespace Domain.Rules.Eta;

// One walk of the forecast: the driver's clock carried down the road, leg
// by leg and stop by stop, through the load being driven and then the ones
// booked after it.
//
// A walk that reaches something it cannot forecast - a region whose rules
// need verification, a cycle scenario that does not exist - stops there and
// says why. Everything before that point stands: a dispatcher is owed the
// arrivals that are known, not a blank because one further on is not.
public sealed class EtaWalk(
  HosTravelClock clock,
  IRouteRegionLookup regions,
  EtaPlanningOptions planning,
  RoutePlanningState state,
  RoutePlan plan,
  EtaChainPlan? chain,
  DateTime now,
  HosCycleMode cycleMode,
  CancellationToken cancellationToken
)
{
  private readonly HashSet<Guid> visited = [];

  public List<StopEta> Results { get; } = [];

  // Loads the walk could not reach, each with the reason.
  public Dictionary<Guid, string> Pending { get; } = [];
  public string? Blocked { get; private set; }

  public void Run(EtaRouteTiming timing, double progress)
  {
    var activeFacility = plan.Stops.FirstOrDefault(stop =>
      chain?.CurrentActivities.GetValueOrDefault(stop.Id)
        is { ArrivedAt: { } at, Completed: false }
      && at <= now
      && state.Progress?.Position is { } position
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
    for (var i = 0; i < timing.Legs.Length && Blocked is null; i++)
    {
      var leg = timing.Legs[i];
      if (leg.EndMiles < progress)
        continue;
      if (!Travel(leg, progress))
      {
        Blocked ??=
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
    if (Blocked is not null)
      Pending[plan.DispatchId] = Blocked;
    foreach (var next in chain?.Future ?? [])
    {
      Blocked ??= next.UnavailableReason;
      if (Blocked is null && next.Connection is { } connection)
        foreach (var leg in connection.Legs)
          if (!Travel(leg, 0))
          {
            Blocked ??=
              "ETA unavailable: the preceding connection needs regional verification.";
            break;
          }
      if (
        Blocked is null
        && (
          next.Route is null || next.Stops.Count != next.Route.Legs.Length + 1
        )
      )
        Blocked =
          "ETA unavailable: the saved route does not match this load's stops.";
      if (Blocked is null)
      {
        Attempt(() => Visit(next.DispatchId, next.Stops[0]));
        for (var i = 0; i < next.Route!.Legs.Length && Blocked is null; i++)
        {
          if (!Travel(next.Route.Legs[i], 0))
          {
            Blocked ??=
              "ETA unavailable: the saved route needs regional verification.";
            break;
          }
          Attempt(() => Visit(next.DispatchId, next.Stops[i + 1]));
        }
      }
      if (Blocked is not null)
        Pending[next.DispatchId] = Blocked;
    }
  }

  private bool Attempt(Action action)
  {
    if (Blocked is not null)
      return false;
    try
    {
      action();
      return true;
    }
    catch (CycleScenarioUnavailableException error)
    {
      Blocked = error.Message;
      return false;
    }
  }

  private bool Travel(EtaRouteTimingLeg leg, double remainingFrom)
  {
    if (leg.Miles == 0 && leg.Seconds == 0)
      return true;
    var hoursPerMile = planning.TravelHours(leg.Miles, leg.Seconds) / leg.Miles;
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

  // Arriving at one stop: when, against which appointment, what the wait
  // and the service do to the driver's clocks, and what is recorded of it.
  private void Visit(Guid dispatchId, PlanStop stop)
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
      && state.Progress?.Position is { } position
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
    Results.Add(
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
