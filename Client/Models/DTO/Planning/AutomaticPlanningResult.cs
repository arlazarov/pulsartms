namespace Client.Models.DTO.Planning;

public sealed record AutomaticPlanningResult(Guid TruckId, Guid? DispatchId, int? LoadNumber,
  RoutePlanningState? State, string? Message)
{
  public DriverHosClocks? Hos { get; init; }
}
