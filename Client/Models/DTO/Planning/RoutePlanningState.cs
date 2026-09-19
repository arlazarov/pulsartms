namespace Client.Models.DTO.Planning;

public sealed record RoutePlanningState(
  TruckRouteProfile Profile,
  RoutePlan? Plan,
  RouteProgress? Progress,
  double? FuelPercent,
  DateTime? FuelUpdatedAt,
  bool ApiConfigured
)
{
  public DispatchEta? Eta { get; init; }
  public long RouteChoiceRevision { get; init; }
  public List<FuelStopArrival> FuelStopArrivals { get; set; } = [];
}
