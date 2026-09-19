namespace Application.Features.Routing.Models;

public sealed record RouteBuildRequest(
  TruckRouteProfile Profile,
  bool FromCurrentPosition = false,
  int? NextStopSequence = null,
  Guid? ExecutionLegId = null
);
