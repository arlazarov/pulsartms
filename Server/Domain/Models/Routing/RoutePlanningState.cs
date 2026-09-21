using Domain.Models.Eta;

namespace Domain.Models.Routing;

public sealed record RoutePlanningState(
  TruckRouteProfile Profile,
  RoutePlan? Plan,
  RouteProgress? Progress,
  double? FuelPercent,
  DateTime? FuelUpdatedAt,
  bool ApiConfigured
)
{
  public SavedRoadVersion? SavedRoad { get; init; }
  public DispatchEta? Eta { get; init; }
  public long RouteChoiceRevision { get; init; }
  public List<FuelStopArrival> FuelStopArrivals { get; set; } = [];
}
