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
}
