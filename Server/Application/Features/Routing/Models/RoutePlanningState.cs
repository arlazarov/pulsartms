namespace Application.Features.Routing.Models;

public sealed record RoutePlanningState(TruckRouteProfile Profile, RoutePlan? Plan, RouteProgress? Progress,
  double? FuelPercent, DateTime? FuelUpdatedAt, bool ApiConfigured)
{
  public Application.Features.Eta.Models.DispatchEta? Eta { get; init; }
}
