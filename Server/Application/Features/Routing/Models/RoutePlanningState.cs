using Application.Features.Eta.Models;

namespace Application.Features.Routing.Models;

public sealed record RoutePlanningState(
  TruckRouteProfile Profile,
  RoutePlan? Plan,
  RouteProgress? Progress,
  double? FuelPercent,
  DateTime? FuelUpdatedAt,
  bool ApiConfigured
)
{
  internal SavedRoadVersion? SavedRoad { get; init; }
  public DispatchEta? Eta { get; init; }
  public long RouteChoiceRevision { get; init; }
  public List<FuelStopArrival> FuelStopArrivals { get; set; } = [];
}
