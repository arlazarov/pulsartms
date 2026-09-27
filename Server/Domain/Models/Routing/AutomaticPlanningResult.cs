using Domain.Models.Fleet;

namespace Domain.Models.Routing;

public sealed record AutomaticPlanningResult(
  Guid TruckId,
  Guid? DispatchId,
  int? LoadNumber,
  RoutePlanningState? State,
  string? Message
)
{
  public DateTimeOffset? CalculatedAt { get; init; }
  public bool IsRefreshing { get; init; }
  public FuelCalculationStatus? FuelStatus { get; init; }
  public DriverHosClocks? Hos { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }

  // What the plan has to say about the loads it was made from, by kind:
  // a screen for the load shows the whole of it, the map a word and a way
  // there. The message stays what it was without them.
  public IReadOnlyList<PlanningNotice> Notices { get; init; } = [];

  // The truck's work planning has passed without a delivery, from the
  // same inputs the summary was read with (WorkPlacements.Conflicts): the
  // map shows it beside the current work, as the board does.
  public IReadOnlyList<WorkConflictNotice> WorkConflicts { get; init; } = [];
}

public sealed record WorkConflictNotice(
  Guid DispatchId,
  Guid? ExecutionLegId,
  int LoadNumber,
  string Conflict
);

// A notice about one load of the plan. SourceReview: the load's source
// changed or is ambiguous and the assignment needs a dispatcher's review;
// the calculation used the accepted assignment.
public sealed record PlanningNotice(
  string Kind,
  int? LoadNumber,
  Guid? DispatchId,
  string Text
)
{
  public const string SourceReview = "source-review";
}
