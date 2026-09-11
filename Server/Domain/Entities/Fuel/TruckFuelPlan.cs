namespace Domain.Entities.Fuel;

public sealed class TruckFuelPlan : BaseEntity
{
  public Guid TruckId { get; set; }
  public Guid RootDispatchId { get; set; }
  public DateTime CalculatedAt { get; set; }
  public string SummaryJson { get; set; } = "";
  public string? CheckedRouteJson { get; set; }
}
