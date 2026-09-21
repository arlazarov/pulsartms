namespace Domain.Models.Routing;

public sealed record FuelObservationStamp(
  DateTime? FuelObservedAt,
  double? FuelPercent,
  DateTime? LocationTime,
  RoutePoint? Position
)
{
  public bool Matches(RoutePlanningState state) =>
    FuelPercent == state.FuelPercent
    && Position == state.Progress?.Position
    && state.Progress?.LocationStale != true;

  public static FuelObservationStamp Capture(RoutePlanningState state) =>
    new(
      state.FuelUpdatedAt,
      state.FuelPercent,
      state.Progress?.LocationTime,
      state.Progress?.Position
    );
}
