using Client.Models.DTO.Dispatch;
using Client.Services;

namespace Client.Shared.Dispatch;

public sealed record DispatchBoardRow(
  TruckDispatchBoardResponse Truck,
  DispatchResponse Load
)
{
  // What makes a row on the board one row. A load handed from one truck to
  // another stands under both of them, and a truck that drives two legs of
  // the same load stands against it twice - so a row is the load, the truck
  // it is on, and the leg that truck is driving. Keyed by the load alone,
  // two such rows in one list were the same key, and Blazor threw on every
  // render rather than diff them.
  public string Key => $"{Truck.Key}:{Load.Id}:{Load.ExecutionLegId}";

  public string TruckNumber => Text(Load.TruckNumber, Truck.TruckNumber);
  public string TrailerNumber => Text(Load.TrailerNumber, Truck.TrailerNumber);
  public string DriverName => Text(Load.DriverName, Truck.DriverName);
  public DispatchStopResponse? Origin =>
    Load.Stops.OrderBy(x => x.Sequence).FirstOrDefault(x => !x.DriverOnly);
  public DispatchStopResponse? Destination =>
    Load.Stops.OrderBy(x => x.Sequence).LastOrDefault();
  public bool OriginCompleted => Completed || StopCompleted(Origin);
  public bool DestinationCompleted => Completed || StopCompleted(Destination);
  public DateOnly? DeliveryDate =>
    Destination?.ScheduledDate ?? Load.DeliveryDate;
  public DateOnly? PickupDate => Origin?.ScheduledDate ?? Load.ShipDate;
  public bool Completed => IsCompleted(Load);
  public bool InTransit =>
    !Completed
    && (
      Load.Status == "in_transit"
      || Load.Stops.Any(x =>
        x.PickedUpAt.HasValue || x.ManualCompletedAt.HasValue
      )
    );
  public bool Planned =>
    Load.Status.Equals("planned", StringComparison.OrdinalIgnoreCase)
    || Load.Status.Equals("unassigned", StringComparison.OrdinalIgnoreCase);

  public int Column(DateOnly today) =>
    InTransit ? 1
    : Planned ? 0
    : IsTodayOrTomorrow(PickupDate, today)
    || IsTodayOrTomorrow(DeliveryDate, today)
      ? 2
    : 0;

  public static bool IsTodayOrTomorrow(DateOnly? date, DateOnly today) =>
    date is { } value && value.DayNumber - today.DayNumber is >= 0 and <= 1;

  public string Status =>
    Completed ? "Completed"
    : Load.Status.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
      ? "Unassigned"
    : Planned ? "Planned"
    : InTransit ? "In transit"
    : "Awaiting pickup";
  public string MapUrl =>
    $"/fleet/map?truckId={Load.TruckId ?? Truck.TruckId}&dispatchId={Load.Id}";

  public static bool IsCompleted(DispatchResponse load) =>
    load.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
    || load.Stops.OrderBy(stop => stop.Sequence).LastOrDefault() is { } final
      && (
        final.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
        || final.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
      )
      && (
        final.CompletionOverride == true
        || final.CompletionOverride != false
          && (
            (final.DeliveredAt ?? final.DepartedAt).HasValue
            || final.ManualCompletedAt.HasValue
              && load.Stops.Where(s => !s.DriverOnly).All(s => s.IsCompleted)
          )
      );

  private static bool StopCompleted(DispatchStopResponse? stop) =>
    stop?.IsCompleted == true;

  public static string Text(string? value, string? fallback = null) =>
    !string.IsNullOrWhiteSpace(value) ? value
    : !string.IsNullOrWhiteSpace(fallback) ? fallback
    : "—";

  public static string Location(DispatchStopResponse? stop) =>
    stop is null
      ? "Pending"
      : Text(
        string.Join(
          ", ",
          new[] { stop.City, stop.Province }.Where(x =>
            !string.IsNullOrWhiteSpace(x)
          )
        ),
        stop.Address
      );

  public static string Schedule(
    DispatchStopResponse? stop,
    DateOnly? fallback = null
  )
  {
    var schedule = StopAppointmentDisplay.Format(
      stop?.ScheduledDate ?? fallback,
      stop?.ScheduledTime,
      stop?.IsWindow == true ? stop.ScheduledDate2 : null,
      stop?.IsWindow == true ? stop.ScheduledTime2 : null
    );
    return schedule == "—" ? "Date pending" : schedule;
  }
}
