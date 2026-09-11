namespace Application.Features.Routing.Models;

public sealed class RoutePlan
{
  public Guid Id { get; set; }
  public Guid DispatchId { get; set; }
  public Guid TruckId { get; set; }
  public int Version { get; set; }
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
  public bool GeometryOmitted { get; set; }
  public DateTime CalculatedAt { get; set; }
  public double OriginalPlannedMiles { get; set; }
  public bool FromCurrentPosition { get; set; }
  public List<PlanStop> Stops { get; set; } = [];
  public TruckRoute Route { get; set; } = new();
  public TruckRouteProfile Profile { get; set; } = new();
  public bool InputsChanged { get; set; }
  public FuelPlan? FuelPlan { get; set; }
  public FuelRecommendations? FuelRecommendations { get; set; }
  public TruckRoute? ReferenceRoute { get; set; }
  public List<PlanStop>? ReferenceStops { get; set; }
  public RouteStopTracking Tracking { get; set; } = new();
  public DateTime? LastReroutedAt { get; set; }
  public RoutePoint? LastReroutePosition { get; set; }
}
