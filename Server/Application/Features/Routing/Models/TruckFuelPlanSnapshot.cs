namespace Application.Features.Routing.Models;

public sealed record FuelItineraryStop(Guid DispatchId, PlanStop Stop, double EndMiles);

public sealed record TruckFuelPlanSnapshot(Guid TruckId, Guid RootDispatchId, DateTime CalculatedAt,
  FuelPlan Plan, IReadOnlyList<FuelItineraryStop> Stops, TruckRoute? CheckedRoute)
{
  public TruckRoute? BaselineRoute { get; init; }
}
