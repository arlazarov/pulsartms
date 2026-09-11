using Application.Features.Routing.Models;

namespace Application.Features.Eta.Models;

public sealed record EtaRootRouteMetadata(string InputHash, Guid TruckId, Guid PlanTruckId, Guid PlanId,
  int Version, RouteStopTracking Tracking, DateTime? FuelCalculatedAt);
