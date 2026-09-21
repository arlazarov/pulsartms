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
  public FuelCalculationStatus? FuelStatus { get; init; }
  public DriverHosClocks? Hos { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public long AssignmentRevision { get; init; }
}
