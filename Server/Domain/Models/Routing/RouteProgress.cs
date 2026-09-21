namespace Domain.Models.Routing;

public sealed record RouteProgress(
  double? ProgressMiles,
  double? RemainingMiles,
  double? RemainingSeconds,
  double DistanceFromRouteMiles,
  bool OffRoute,
  bool LocationStale,
  DateTime? LocationTime,
  RoutePoint? Position
);
