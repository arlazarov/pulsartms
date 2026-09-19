using Client.Models.DTO.Planning;

namespace Client.Pages.FleetMap;

internal sealed class FleetRouteDisplayMemory
{
  private (
    Guid Truck,
    Guid Dispatch,
    Guid? ExecutionLeg,
    long AssignmentRevision,
    Guid? Stop,
    string Passed,
    bool Completed
  )? identity;
  private PlanStop[] stops = [];
  private (Guid Plan, int Version)? geometry;
  private (Guid Plan, int Version)? previousGeometry;
  private double nextStopRouteMiles;
  private DispatchEta? previousEta;
  private DateTime? changedStopForecast;
  private RouteProgress? previousProgress;
  public double? RetainedRemainingMiles => previousProgress?.RemainingMiles;
  public double? RetainedNextStopMiles { get; private set; }

  private static (Guid, Guid, Guid?, long, Guid?, string, bool)? Identity(
    RoutePlanningState? state
  ) =>
    state?.Plan is { } plan
      ? (
        plan.TruckId,
        plan.DispatchId,
        plan.ExecutionLegId,
        plan.AssignmentRevision,
        plan.Tracking.NextStopId,
        string.Join(",", plan.Tracking.PassedStopIds.Order()),
        plan.Tracking.AllStopsPassed
      )
      : null;

  private static IEnumerable<PlanStop> StopContext(RoutePlanningState? state) =>
    (state?.Plan?.Stops ?? [])
      .Concat(state?.Plan?.ReferenceStops ?? [])
      .Select(stop => stop with { Name = "", Commodity = "", Notes = "" });

  public bool Matches(RoutePlanningState? state) =>
    identity == Identity(state) && stops.SequenceEqual(StopContext(state));

  public bool MatchesGeometry(RoutePlanningState? state) =>
    geometry == Geometry(state);

  private static (Guid, int)? Geometry(RoutePlanningState? state) =>
    state?.Plan is { } plan ? (plan.Id, plan.Version) : null;

  private bool IsUpdating(RoutePlanningState? state, bool refreshing = false) =>
    Matches(state)
    && state?.Plan is { InputsChanged: false, Tracking.AllStopsPassed: false }
    && (
      refreshing
      || state.Eta?.RouteUpdatePending == true
      || state.Eta is null && previousEta is not null
    );

  public bool IsRetaining(
    RoutePlanningState? state,
    DateTime now,
    bool refreshing = false
  ) =>
    IsUpdating(state, refreshing)
    && previousEta is { } eta
    && now < eta.ValidUntil.ToUniversalTime().AddMinutes(15);

  public static bool CanDisplay(
    DispatchEta? eta,
    DateTime now,
    bool refreshing = false
  ) =>
    eta is not null
    && now
      < eta.ValidUntil.ToUniversalTime()
        .AddMinutes(eta.RouteUpdatePending || refreshing ? 15 : 0);

  public void Update(
    RoutePlanningState? state,
    DateTime now,
    double? remainingMiles = null,
    double? progressMiles = null
  )
  {
    var nextIdentity = Identity(state);
    if (identity != nextIdentity)
      changedStopForecast = null;
    else if (
      (!Matches(state) || state?.Plan?.InputsChanged == true)
      && previousEta is { } previous
    )
      changedStopForecast = previous.CalculatedAt;
    if (!Matches(state))
    {
      previousEta = null;
      previousProgress = null;
      previousGeometry = null;
      RetainedNextStopMiles = null;
      remainingMiles = progressMiles = null;
    }
    identity = nextIdentity;
    stops = StopContext(state).ToArray();
    geometry = Geometry(state);
    if (
      state?.Plan is null
      || state.Plan.InputsChanged
      || state.Plan.Tracking.AllStopsPassed
    )
    {
      previousEta = null;
      previousProgress = null;
      previousGeometry = null;
      RetainedNextStopMiles = null;
      return;
    }
    if (changedStopForecast is { } changedAt)
    {
      if (
        state.Eta is not { RouteUpdatePending: false, Stops.Count: > 0 } current
        || current.CalculatedAt <= changedAt
      )
        return;
      changedStopForecast = null;
    }
    if (IsUpdating(state))
    {
      // Pending responses must not renew the original forecast's grace
      // deadline.
      if (
        previousEta is null
        && state.Eta is { Stops.Count: > 0 } pending
        && CanDisplay(pending, now)
      )
        previousEta = pending;
      if (previousProgress is null)
      {
        previousProgress = state.Progress;
        previousGeometry = geometry;
        nextStopRouteMiles = state.Plan.NextStopRouteMiles;
        RetainedNextStopMiles = state.Plan.RemainingToNextStop(
          previousProgress?.ProgressMiles
        );
      }
      return;
    }
    previousEta =
      state.Eta is { Stops.Count: > 0 } eta && CanDisplay(eta, now)
        ? eta
        : null;
    previousProgress = state.Progress;
    previousGeometry = geometry;
    nextStopRouteMiles = state.Plan.NextStopRouteMiles;
    RetainedNextStopMiles = state.Plan.RemainingToNextStop(
      previousProgress?.ProgressMiles
    );
    RecordProgress(remainingMiles, progressMiles);
  }

  public void RecordProgress(double? remainingMiles, double? progressMiles)
  {
    if (
      remainingMiles is not { } remaining
      || progressMiles is not { } progress
      || !double.IsFinite(remaining)
      || !double.IsFinite(progress)
    )
      return;
    previousProgress = previousProgress is { } saved
      ? saved with
      {
        RemainingMiles = remaining,
        ProgressMiles = progress,
      }
      : new(progress, remaining, null, 0, false, false, null, null);
    RetainedNextStopMiles = Math.Max(0, nextStopRouteMiles - progress);
  }

  public RoutePlanningState? Display(
    RoutePlanningState? state,
    DateTime now,
    bool refreshing = false
  )
  {
    if (state?.Plan?.InputsChanged == true)
      return state with { Eta = null, Progress = null };
    if (changedStopForecast is not null && state?.Eta is { Stops.Count: > 0 })
      return state with { Eta = null };
    if (
      state is not null
      && IsUpdating(state, refreshing)
      && previousEta is { } previous
      && now >= previous.ValidUntil.ToUniversalTime().AddMinutes(15)
    )
      return state with { Eta = null };
    if (!IsRetaining(state, now, refreshing) || state is null)
      return state;
    // Distances can stay visible across a reroute, but progress coordinates
    // belong to one geometry only.
    return state with
    {
      Eta = previousEta! with { RouteUpdatePending = true },
      Progress =
        previousGeometry == Geometry(state)
          ? previousProgress ?? state.Progress
          : state.Progress,
    };
  }
}
