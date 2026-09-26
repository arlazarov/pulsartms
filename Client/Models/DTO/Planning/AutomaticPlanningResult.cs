namespace Client.Models.DTO.Planning;

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
  public Guid? ExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }
  public DriverHosClocks? Hos { get; init; }

  // What the plan has to say about the loads it was made from, by kind.
  public List<PlanningNotice> Notices { get; init; } = [];
}

public sealed record PlanningNotice(
  string Kind,
  int? LoadNumber,
  Guid? DispatchId,
  string Text
)
{
  public const string SourceReview = "source-review";
}
