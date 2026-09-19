using Application.Features.Eta.Models;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelScheduleContext
{
  private const int MaximumRoutes = 12;
  private const int MaximumStops = 40;
  private const int MaximumGeometryPoints = 100_000;
  private readonly EtaService eta;
  private readonly RoutePlanningState source;
  private readonly FuelItineraryStop[] itinerary;
  private readonly DateTime now;
  private readonly DriverHosClocks? clocks;
  private readonly HosHistory? history;
  private readonly DispatchEta baseline;
  private readonly string? unavailable;
  private int checkedRoutes = 1;

  public FuelScheduleImpact Baseline { get; }

  internal FuelScheduleContext(
    EtaService eta,
    RoutePlanningState state,
    TruckRoute baselineRoute,
    IReadOnlyList<FuelItineraryStop> itinerary,
    DateTime now,
    DriverHosClocks? clocks,
    HosHistory? history,
    string? unavailable,
    CancellationToken cancellationToken
  )
  {
    this.eta = eta;
    source = state with
    {
      Plan = state.Plan is null
        ? null
        : new RoutePlan
        {
          TruckId = state.Plan.TruckId,
          ExecutionLegId = state.Plan.ExecutionLegId,
          AssignmentRevision = state.Plan.AssignmentRevision,
        },
    };
    this.itinerary = itinerary.Count <= MaximumStops ? itinerary.ToArray() : [];
    this.now = now;
    this.clocks = clocks;
    this.history = history;
    this.unavailable = unavailable;
    baseline = unavailable is null
      ? Replay(baselineRoute, cancellationToken)
      : Missing(unavailable);
    Baseline = Compare(baseline);
  }

  public FuelScheduleImpact Evaluate(
    TruckRoute checkedCollapsedRoute,
    CancellationToken cancellationToken = default
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (unavailable is not null)
      return Baseline;
    var version = Interlocked.Increment(ref checkedRoutes);
    if (version > MaximumRoutes)
      return Compare(Missing("Schedule preview route limit reached."));
    if (
      ValidateRoute(
        checkedCollapsedRoute,
        itinerary,
        source.Progress!.Position!
      ) is
      { } invalid
    )
      return Compare(Missing(invalid));
    return Compare(Replay(checkedCollapsedRoute, cancellationToken));
  }

  internal static string? ValidateInputs(
    RoutePlanningState state,
    TruckRoute route,
    IReadOnlyList<FuelItineraryStop> itinerary,
    DateTime now
  )
  {
    if (
      state.Plan is not { InputsChanged: false } plan
      || plan.TruckId == Guid.Empty
      || state.Progress is not { LocationStale: false, Position.IsValid: true }
    )
      return "Current route or GPS unavailable.";
    if (
      now > DateTime.MaxValue.AddDays(-91)
      || itinerary.Count is 0 or > MaximumStops
      || itinerary.Any(stop =>
        stop.DispatchId == Guid.Empty || stop.Stop.Id == Guid.Empty
      )
      || !MatchesAssignments(plan, itinerary)
      || itinerary.Select(stop => stop.Stop.Id).Distinct().Count()
        != itinerary.Count
    )
      return "Fuel itinerary needs verification.";
    if (ValidateRoute(route, itinerary, state.Progress.Position) is { } invalid)
      return invalid;
    double end = 0;
    for (var index = 0; index < itinerary.Count; index++)
    {
      end += route.Legs[index].Miles;
      if (
        !double.IsFinite(itinerary[index].EndMiles)
        || Math.Abs(end - itinerary[index].EndMiles) > .01
      )
        return "Fuel itinerary boundaries need verification.";
    }
    return null;
  }

  private static bool MatchesAssignments(
    RoutePlan plan,
    IReadOnlyList<FuelItineraryStop> itinerary
  )
  {
    if (itinerary[0].DispatchId != plan.DispatchId)
      return false;
    var seen = new HashSet<Guid>();
    Guid? previous = null;
    foreach (var stop in itinerary)
    {
      if (stop.DispatchId != previous)
      {
        if (!seen.Add(stop.DispatchId))
          return false;
        previous = stop.DispatchId;
      }
      if (
        (
          stop.DispatchId == plan.DispatchId
            ? stop.ExecutionLegId != plan.ExecutionLegId
              || stop.AssignmentRevision != plan.AssignmentRevision
            : stop.ExecutionLegId.HasValue || stop.AssignmentRevision != 0
        )
      )
        return false;
    }
    return true;
  }

  private static string? ValidateRoute(
    TruckRoute route,
    IReadOnlyList<FuelItineraryStop> itinerary,
    RoutePoint start
  )
  {
    if (
      route.Legs.Count != itinerary.Count
      || !double.IsFinite(route.Miles)
      || route.Miles < 0
      || !double.IsFinite(route.Seconds)
      || route.Seconds is < 0 or > 90 * 24 * 3600
    )
      return "Complete checked fuel route unavailable.";
    double miles = 0,
      seconds = 0;
    long points = 0;
    for (var index = 0; index < route.Legs.Count; index++)
    {
      var leg = route.Legs[index];
      points += leg.Points.Count;
      if (
        points > MaximumGeometryPoints
        || leg.Points.Count < 2
        || !double.IsFinite(leg.Miles)
        || !double.IsFinite(leg.Seconds)
        || leg.Miles < 0
        || leg.Seconds < 0
        || leg.Points.Any(point => !point.IsValid)
        || RouteGeometry.Distance(
          leg.Points[0],
          index == 0 ? start : itinerary[index - 1].Stop.Point
        ) > .5
        || RouteGeometry.Distance(leg.Points[^1], itinerary[index].Stop.Point)
          > .5
      )
        return "Fuel route stop boundaries or travel times need verification.";
      miles += leg.Miles;
      seconds += leg.Seconds;
    }
    return
      !double.IsFinite(miles)
      || !double.IsFinite(seconds)
      || Math.Abs(miles - route.Miles) > .01
      || Math.Abs(seconds - route.Seconds) > 1
      ? "Fuel route totals need verification."
      : null;
  }

  private DispatchEta Replay(
    TruckRoute route,
    CancellationToken cancellationToken,
    IReadOnlyList<FuelItineraryStop>? visits = null
  )
  {
    visits ??= itinerary;
    var plan = new RoutePlan
    {
      DispatchId = visits[0].DispatchId,
      TruckId = source.Plan!.TruckId,
      ExecutionLegId = source.Plan.ExecutionLegId,
      AssignmentRevision = source.Plan.AssignmentRevision,
      FromCurrentPosition = true,
      Route = route,
      Stops = visits.Select(stop => stop.Stop).ToList(),
      Profile = source.Profile,
    };
    var state = new RoutePlanningState(
      source.Profile,
      plan,
      new(
        0,
        route.Miles,
        route.Seconds,
        0,
        false,
        false,
        source.Progress!.LocationTime,
        source.Progress.Position
      ),
      source.FuelPercent,
      source.FuelUpdatedAt,
      source.ApiConfigured
    );
    // Fuel previews do not populate live ETA results or route-timing caches.
    return eta.CalculateRoadPreview(
      state,
      clocks,
      now,
      history,
      cancellationToken
    );
  }

  private DispatchEta Missing(string reason) => new(now, now, [], reason, []);

  private FuelScheduleImpact Compare(DispatchEta candidate)
  {
    var baselineStops = baseline.Stops.ToDictionary(stop => stop.StopId);
    var candidateStops = candidate.Stops.ToDictionary(stop => stop.StopId);
    var stops = itinerary
      .Select(stop =>
      {
        var before = baselineStops.GetValueOrDefault(stop.Stop.Id);
        var after = candidateStops.GetValueOrDefault(stop.Stop.Id);
        return new FuelStopScheduleImpact(
          stop.DispatchId,
          stop.Stop.Id,
          before?.Arrival,
          after?.Arrival,
          before?.LateMinutes,
          after?.LateMinutes,
          before?.LateMinutes is { } oldLate
          && after?.LateMinutes is { } newLate
            ? Math.Max(0, newLate - oldLate)
            : null,
          before?.Hours?.CycleAtArrivalMinutes,
          after?.Hours?.CycleAtArrivalMinutes,
          Known(before) && Known(after),
          Short(before),
          Short(after)
        );
      })
      .ToArray();
    var complete =
      stops.Length > 0
      && stops.All(stop =>
        stop.BaselineArrival.HasValue && stop.CandidateArrival.HasValue
      );
    var known = complete && stops.All(stop => stop.CycleKnown);
    var last = stops.LastOrDefault();
    int? addedMinutes =
      complete
      && last
        is { BaselineArrival: { } oldArrival, CandidateArrival: { } newArrival }
        ? (int)Math.Max(0, Math.Ceiling((newArrival - oldArrival).TotalMinutes))
        : null;
    int? addedLate =
      complete && stops.All(stop => stop.AddedLateMinutes.HasValue)
        ? stops.Max(stop => stop.AddedLateMinutes)
        : null;
    var reason =
      candidate.UnavailableReason
      ?? baseline.UnavailableReason
      ?? (
        !complete ? "Complete schedule preview unavailable."
        : !known ? "Cycle history unavailable or unverified."
        : null
      );
    return new(
      now,
      complete,
      known,
      stops.Any(stop => stop.BaselineCycleShort),
      stops.Any(stop => stop.CycleShort),
      addedMinutes,
      addedLate,
      stops,
      reason
    );
  }

  private static bool Known(StopEta? stop) =>
    stop?.Hours is { CycleVerified: true, CycleAtArrivalMinutes: not null };

  private static bool Short(StopEta? stop) =>
    stop?.Hours is { CycleVerified: true } hours
    && (
      hours.FirstCycleShortageAt.HasValue
      || hours.DrivingShortfallMinutes is > 0
      || hours.CycleAtArrivalMinutes is < 0
    );
}
