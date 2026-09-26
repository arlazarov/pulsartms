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

  // Two results are the same result when they say the same things: the
  // list of notices compares by its notices, not by which list it is,
  // as the cache of previews expects a copied result to equal its source.
  public bool Equals(AutomaticPlanningResult? other) =>
    other is not null
    && TruckId == other.TruckId
    && DispatchId == other.DispatchId
    && LoadNumber == other.LoadNumber
    && Equals(State, other.State)
    && Message == other.Message
    && CalculatedAt == other.CalculatedAt
    && IsRefreshing == other.IsRefreshing
    && Equals(FuelStatus, other.FuelStatus)
    && ExecutionLegId == other.ExecutionLegId
    && AssignmentRevision == other.AssignmentRevision
    && Equals(Hos, other.Hos)
    && Notices.SequenceEqual(other.Notices);

  public override int GetHashCode() =>
    HashCode.Combine(
      TruckId,
      DispatchId,
      LoadNumber,
      Message,
      CalculatedAt,
      ExecutionLegId,
      AssignmentRevision,
      Notices.Count
    );
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
