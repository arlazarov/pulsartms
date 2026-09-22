using System.Text.Json.Serialization;
using Domain.Rules.Routing;

namespace Domain.Models.Routing;

public sealed class RoutePlan
{
  [JsonIgnore]
  public List<RouteMovement> CompletedMovement { get; } = [];
  public Guid Id { get; set; }
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public Guid TruckId { get; set; }
  public int Version { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool GeometryOmitted { get; set; }
  public DateTime CalculatedAt { get; set; }
  public double OriginalPlannedMiles { get; set; }
  public bool FromCurrentPosition { get; set; }
  public List<PlanStop> Stops { get; set; } = [];
  public List<RouteSegmentMeaning> Segments =>
    RouteSegmentClassification.Read(this);
  public string ReferenceGeometrySource => "EstimatedRoad";
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
