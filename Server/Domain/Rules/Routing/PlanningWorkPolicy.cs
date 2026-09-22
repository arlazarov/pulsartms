using Domain.Entities.Execution;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

public static class PlanningWorkPolicy
{
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
    var warning =
      $"Load {segment.LoadNumber}: "
      + (segment.SourceReviewReason ?? "Source changes need review.")
      + " Calculation uses the accepted assignment.";
    return result with
    {
      Message = string.IsNullOrWhiteSpace(result.Message)
        ? warning
        : $"{result.Message} {warning}",
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
