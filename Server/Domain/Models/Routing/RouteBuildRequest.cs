namespace Domain.Models.Routing;

public sealed record RouteBuildRequest(
  TruckRouteProfile Profile,
  bool FromCurrentPosition = false,
  int? NextStopSequence = null,
  Guid? ExecutionLegId = null
);
