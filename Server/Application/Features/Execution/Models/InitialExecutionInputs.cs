using Domain.Entities.Dispatch;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Domain.Models.Execution;

public sealed record InitialExecutionInputs(
  IReadOnlyList<DispatchStop> Stops,
  string? ReviewReason
)
{
  public static InitialExecutionInputs Resolve(
    DispatchEntity load,
    IReadOnlyList<DispatchStop> stops,
    DateTime now
  )
  {
    stops = stops.Select(ExecutionSnapshots.Copy).ToArray();
    foreach (var stop in stops)
    {
      if (stop.DriverId is null && string.IsNullOrWhiteSpace(stop.DriverName))
      {
        stop.DriverId = load.DriverId;
        stop.DriverName = load.DriverName;
      }
      if (
        stop.TrailerId is null
        && string.IsNullOrWhiteSpace(stop.TrailerNumber)
      )
      {
        stop.TrailerId = load.TrailerId;
        stop.TrailerNumber = load.TrailerNumber;
      }
    }
    if (
      stops.Count is < 1 or > 49
      || stops.Select(x => x.Id).Distinct().Count() != stops.Count
      || stops.Any(x => x.Id == Guid.Empty || x.StateAfter == "No truck")
      || stops[0].TruckId is not { } truck
      || stops.Any(x =>
        x.TruckId != truck
        || x.DriverId != stops[0].DriverId
        || x.CoDriverId != stops[0].CoDriverId
        || x.TrailerId != stops[0].TrailerId
        || x.DriverId is null && !string.IsNullOrWhiteSpace(x.DriverName)
        || x.CoDriverId is null && !string.IsNullOrWhiteSpace(x.CoDriverName)
        || x.TrailerId is null && !string.IsNullOrWhiteSpace(x.TrailerNumber)
      )
    )
      return new(
        stops,
        "Review the initial assignment: all truck visits need one resolved resource set. Separate driver travel and resource changes require explicit execution boundaries."
      );
    if (
      !ExecutionActualChronology.Ordered(stops)
      || stops.Any(x =>
        new[]
        {
          x.ArrivedAt,
          x.PickedUpAt,
          x.DeliveredAt,
          x.DepartedAt,
          x.ManualCompletedAt,
        }.Any(t => t > now || t?.Year < 2000)
        || x.ArrivedAt > x.PickedUpAt
        || (x.PickedUpAt ?? x.ArrivedAt) > x.DeliveredAt
        || (x.DeliveredAt ?? x.PickedUpAt ?? x.ArrivedAt) > x.DepartedAt
        || x.ManualCompletedAt < x.ArrivedAt
      )
    )
      return new(
        stops,
        "Review actual visit times before accepting the initial assignment."
      );
    return new(stops, null);
  }
}
