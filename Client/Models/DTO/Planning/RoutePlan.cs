namespace Client.Models.DTO.Planning;

public sealed class RoutePlan
{
  public Guid Id { get; set; }
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public Guid TruckId { get; set; }
  public int Version { get; set; }
  public bool GeometryOmitted { get; set; }
  public DateTime CalculatedAt { get; set; }
  public double OriginalPlannedMiles { get; set; }
  public bool FromCurrentPosition { get; set; }
  public List<PlanStop> Stops { get; set; } = [];
  public List<RouteSegmentMeaning> Segments { get; set; } = [];
  public string ReferenceGeometrySource { get; set; } = "EstimatedRoad";
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
  public int NextStopLegCount =>
    Math.Clamp(
      Stops.FindIndex(s => s.Id == Tracking.NextStopId)
        + (FromCurrentPosition ? 1 : 0),
      0,
      Route.Legs.Count
    );
  public double NextStopRouteMiles =>
    Route.Legs.Take(NextStopLegCount).Sum(l => l.Miles);
  public double? NextStopDistance =>
    Tracking.NextStopId.HasValue
      ? Route.Legs.Take(NextStopLegCount).LastOrDefault()?.Miles ?? 0
      : null;

  public double? RemainingToNextStop(double? progress) =>
    Tracking.NextStopId.HasValue && progress.HasValue
      ? Math.Max(0, NextStopRouteMiles - progress.Value)
      : null;
}
