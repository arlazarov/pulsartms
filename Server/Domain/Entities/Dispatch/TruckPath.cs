namespace Domain.Entities.Dispatch;

public sealed record TruckPathResult<T>(
  Guid? TruckId,
  string TruckNumber,
  IReadOnlyList<T> Stops
);

public static class TruckPath
{
  public static TruckPathResult<T> Resolve<T>(
    Guid? truckId,
    string truckNumber,
    Guid? planningTruckId,
    Guid? planningFromStopId,
    IEnumerable<T> stops,
    Func<T, string, string, T> withOperation
  )
    where T : class, ITruckPathStop
  {
    var ordered = stops.Any(s => s.ManualAction is not null)
      ? StopOperation.Resolve(stops, planningFromStopId, withOperation)
      : stops.OrderBy(s => s.Sequence).ToList();
    var start = planningFromStopId.HasValue
      ? ordered.FindIndex(s => s.Id == planningFromStopId)
      : ordered.FindIndex(s =>
        s.TruckId.HasValue
        || !string.IsNullOrWhiteSpace(s.TruckNumber)
        || s.ManualStateAfter is not null and not "No truck"
      );
    // A deleted manual anchor must not silently become a different visit.
    var selected =
      planningFromStopId.HasValue && start < 0
        ? []
        : ordered.Skip(Math.Max(0, start)).ToList();
    selected = selected.SkipWhile(s => s.StateAfter == "No truck").ToList();
    // A personal-travel gap cannot be joined into a continuous truck route.
    if (selected.Any(s => s.StateAfter == "No truck"))
      selected = [];
    if (
      planningTruckId is { } confirmed
      && (
        truckId.HasValue && truckId != confirmed
        || ordered.Any(s =>
          s.StateAfter != "No truck"
          && s.TruckId.HasValue
          && s.TruckId != confirmed
        )
        || start > 0
          && ordered
            .Take(start)
            .Any(s =>
              s.StateAfter != "No truck"
              && (
                s.TruckId.HasValue || !string.IsNullOrWhiteSpace(s.TruckNumber)
              )
            )
      )
    )
      selected = [];
    var resolvedTruck =
      planningTruckId
      ?? truckId
      ?? selected.FirstOrDefault(s => s.TruckId.HasValue)?.TruckId;
    if (string.IsNullOrWhiteSpace(truckNumber))
      truckNumber =
        selected
          .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TruckNumber))
          ?.TruckNumber ?? "";
    return new(resolvedTruck, truckNumber, selected);
  }
}
