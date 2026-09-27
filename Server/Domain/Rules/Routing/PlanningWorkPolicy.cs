using Domain.Entities.Execution;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

public static class PlanningWorkPolicy
{
  // Which work a truck is on: the first candidate whose saved plan has not
  // passed all its stops at the same assignment and inputs, and the
  // candidates passed before it. This is the one rule. Each caller reads
  // the saved plans its own way and hands them in; none walks the
  // candidates itself.
  public static CurrentWorkChoice ChooseCurrent(
    TruckItinerarySnapshot snapshot,
    TruckRouteProfile profile,
    Func<TruckWorkSegment, SavedRoutePlanMetadata?> saved
  )
  {
    var passed = new List<TruckWorkSegment>();
    foreach (var segment in Candidates(snapshot))
    {
      if (!IsPassed(snapshot, segment, saved(segment), profile))
        return new(segment, passed);
      passed.Add(segment);
    }
    return new(null, passed);
  }

  // The assignment revision a piece of work was accepted at, as the
  // itinerary records it: a leg's revision, or an older load's planning
  // revision (its own AssignmentRevision is never stored and reads 0).
  // Every reader comparing a row with the planning inputs compares this.
  public static long AcceptedRevision(
    Guid? executionLegId,
    long assignmentRevision,
    long planningAssignmentRevision
  ) =>
    executionLegId.HasValue ? assignmentRevision : planningAssignmentRevision;

  public static long AcceptedRevision(RouteWorkSnapshot load) =>
    AcceptedRevision(
      load.ExecutionLegId,
      load.AssignmentRevision,
      load.PlanningAssignmentRevision
    );

  // The work after the current, in the itinerary's order (WorkOrderKey,
  // the board's) and with the board's membership: work still open to the
  // truck, overdue work included, as the board shows it. A planned leg of
  // the load the current work delivers is not work after it; one after a
  // hand-over (the current ends in a drop, not a delivery) is.
  public static IEnumerable<TruckWorkSegment> Followers(
    TruckItinerarySnapshot snapshot,
    WorkIdentity? current
  )
  {
    if (
      current is null
      || snapshot.Segments.FirstOrDefault(x => x.Work == current) is not { } now
    )
      return [];
    var delivers = Delivers(now);
    return snapshot
      .Segments.SkipWhile(x => x.Work != current)
      .Skip(1)
      .Where(x =>
        x.Work.ExecutionLegId.HasValue
          ? HasOpenAssignment(x)
            && !(
              delivers
              && x.Work.DispatchId == now.Work.DispatchId
              && x.Status == "planned"
            )
          : x.Status is "assigned" or "in_transit"
      );
  }

  private static bool Delivers(TruckWorkSegment segment) =>
    segment.Work.ExecutionLegId.HasValue
    && CanUseGps(segment)
    && segment
      .Visits.Where(x => x.InTruckPath)
      .OrderBy(x => x.Sequence)
      .LastOrDefault()
      is { } end
    && (end.ManualAction ?? end.Operation) is { } action
    && (
      string.Equals(action, "Drop Off", StringComparison.OrdinalIgnoreCase)
      || string.Equals(action, "Delivery", StringComparison.OrdinalIgnoreCase)
    );

  // Whether planning has moved past a candidate: its saved plan passed all
  // its stops for this assignment and these inputs. A caller that must read
  // the saved plans one at a time, stopping at the current work, asks this
  // in candidate order instead of ChooseCurrent.
  public static bool IsPassed(
    TruckItinerarySnapshot snapshot,
    TruckWorkSegment segment,
    SavedRoutePlanMetadata? saved,
    TruckRouteProfile profile
  ) =>
    IsCompleted(
      saved,
      RouteWorkProjection.Capture(segment, snapshot.Resources.TruckNumber),
      profile
    );

  public static IEnumerable<TruckWorkSegment> Candidates(
    TruckItinerarySnapshot snapshot
  ) =>
    snapshot.Segments.Where(x =>
      x.Work.ExecutionLegId.HasValue
      || x.Status is "assigned" or "in_transit" && !x.IsOverdue
    );

  public static bool CanUseGps(IWorkFacts load) =>
    CanUseGps(load.ExecutionLegId, load.ExecutionStatus, load.AwaitingReceipt);

  public static bool CanUseGps(TruckWorkSegment segment) =>
    CanUseGps(
      segment.Work.ExecutionLegId,
      segment.Status,
      segment.Visits.FirstOrDefault()?.Actuals.AwaitingHandoff == true
    );

  public static bool HasOpenAssignment(TruckWorkSegment segment) =>
    HasOpenAssignment(segment.Work.ExecutionLegId, segment.Status);

  // Where a truck's run ends, asked once for everything that follows a run:
  // the fuel plan chaining its loads, and the arrival forecast chaining
  // theirs. Work accepted into execution ahead of the load in hand is still
  // this truck's work - a leg is how a load is taken on, not a boundary. The
  // boundary is a transfer: a leg that is not this truck's, one no longer
  // open, or one still waiting to be received.
  //
  // It is the same question as whether GPS may be matched to that work, and
  // so the same answer: is this the truck's to be driving.
  public static bool ContinuesTheRun(IWorkFacts next, Guid truckId) =>
    next.TruckId == truckId && CanUseGps(next);

  public static bool ContinuesTheRun(TruckWorkSegment next) => CanUseGps(next);

  private static bool HasOpenAssignment(Guid? legId, string? status) =>
    !legId.HasValue || status is "active" or "planned";

  private static bool CanUseGps(
    Guid? legId,
    string? status,
    bool awaitingReceipt
  ) => !awaitingReceipt && HasOpenAssignment(legId, status);

  public static bool IsCompleted(RoutePlan? plan, RouteWorkSnapshot load) =>
    plan is { Tracking.AllStopsPassed: true, InputsChanged: false }
    && SameAssignment(
      plan.TruckId,
      plan.DispatchId,
      plan.ExecutionLegId,
      plan.AssignmentRevision,
      load
    );

  public static bool IsCompleted(
    SavedRoutePlanMetadata? saved,
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) =>
    saved is { Tracking.AllStopsPassed: true }
    && saved.TruckId == load.TruckId
    && saved.ExecutionLegId == load.ExecutionLegId
    && SameAssignment(
      saved.PlanTruckId,
      saved.PlanDispatchId,
      saved.PlanExecutionLegId,
      saved.AssignmentRevision,
      load
    )
    && RoutePlanInputs.Matches(saved, load, profile);

  private static bool SameAssignment(
    Guid truckId,
    Guid dispatchId,
    Guid? legId,
    long revision,
    RouteWorkSnapshot load
  ) =>
    truckId == load.TruckId
    && dispatchId == load.Id
    && legId == load.ExecutionLegId
    && (!legId.HasValue || revision == load.AssignmentRevision);

  public static WorkReadProblem? BlockingProblem(TruckWorkSegment segment) =>
    segment
      .Problems.Where(problem =>
        problem != WorkReadProblem.SourceReviewRequired
        || !segment.Work.ExecutionLegId.HasValue
      )
      .Select(problem => (WorkReadProblem?)problem)
      .FirstOrDefault();

  public static AutomaticPlanningResult WithWarnings(
    AutomaticPlanningResult result,
    TruckWorkSegment? segment
  )
  {
    if (
      segment is null
      || !segment.Work.ExecutionLegId.HasValue
      || !segment.Problems.Contains(WorkReadProblem.SourceReviewRequired)
    )
      return result;
    // Said as a notice about the load, not in the message: the map showed
    // the whole sentence over the route, where it could not be acted on.
    var text =
      (segment.SourceReviewReason ?? "Source changes need review.")
      + " Calculation uses the accepted assignment.";
    return result with
    {
      Notices =
      [
        .. result.Notices,
        new PlanningNotice(
          PlanningNotice.SourceReview,
          segment.LoadNumber,
          segment.Work.DispatchId,
          text
        ),
      ],
    };
  }

  public static RouteWorkSnapshot Resolve(
    TruckItinerarySnapshot snapshot,
    Guid dispatchId,
    Guid? executionLegId
  ) =>
    Resolve(
      snapshot,
      snapshot.Segments.SingleOrDefault(x =>
        x.Work.DispatchId == dispatchId
        && x.Work.ExecutionLegId == executionLegId
      )
        ?? throw new RoutePlanningException(
          "The truck assignment changed. Refresh before planning its route."
        )
    );

  public static RouteWorkSnapshot Resolve(
    TruckItinerarySnapshot snapshot,
    TruckWorkSegment segment
  )
  {
    var problem = BlockingProblem(segment);
    if (problem.HasValue)
      throw new RoutePlanningException(
        problem.Value switch
        {
          WorkReadProblem.ConflictingAssignment =>
            "This load has multiple truck assignments.",
          WorkReadProblem.SourceReviewRequired =>
            "Review the changed source before planning this assignment.",
          WorkReadProblem.MissingTransfer =>
            "The assignment is missing its transfer record.",
          _ => "The truck itinerary needs review before routing.",
        }
      );
    return RouteWorkProjection.Capture(segment, snapshot.Resources.TruckNumber);
  }
}
