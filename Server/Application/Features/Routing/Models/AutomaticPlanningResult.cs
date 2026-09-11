namespace Application.Features.Routing.Models;

public sealed record AutomaticPlanningResult(Guid TruckId, Guid? DispatchId, int? LoadNumber,
  RoutePlanningState? State, string? Message)
{
  public Application.Features.Fleet.Models.DriverHosClocks? Hos { get; init; }
}
