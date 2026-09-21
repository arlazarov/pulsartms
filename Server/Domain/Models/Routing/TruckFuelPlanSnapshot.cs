namespace Domain.Models.Routing;

public sealed record FuelItineraryStop(
  Guid DispatchId,
  PlanStop Stop,
  double EndMiles
)
{
  public Guid? ExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }
}

public sealed record TruckFuelPlanSnapshot(
  Guid TruckId,
  Guid RootDispatchId,
  DateTime CalculatedAt,
  FuelPlan Plan,
  IReadOnlyList<FuelItineraryStop> Stops,
  TruckRoute? CheckedRoute
)
{
  public TruckRoute? BaselineRoute { get; init; }
  public FuelRoadDependencies? RoadDependencies { get; init; }
  public FuelHistoryDependencies? HistoryDependencies { get; init; }
  public Guid? RootExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }
}
