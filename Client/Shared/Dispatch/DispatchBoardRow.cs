using Client.Models.DTO.Dispatch;
namespace Client.Shared.Dispatch;

public sealed record DispatchBoardRow(TruckDispatchBoardResponse Truck, DispatchResponse Load)
{
  public string TruckNumber => Text(Load.TruckNumber, Truck.TruckNumber);
  public string TrailerNumber => Text(Load.TrailerNumber, Truck.TrailerNumber);
  public string DriverName => Text(Load.DriverName, Truck.DriverName);
  public DispatchStopResponse? Origin => Load.Stops.OrderBy(x => x.Sequence).FirstOrDefault();
  public DispatchStopResponse? Destination => Load.Stops.OrderBy(x => x.Sequence).LastOrDefault();
  public bool OriginCompleted => Completed || StopCompleted(Origin);
  public bool DestinationCompleted => Completed || StopCompleted(Destination);
  public DateOnly? DeliveryDate => Destination?.ScheduledDate ?? Load.DeliveryDate;
  public DateOnly? PickupDate => Origin?.ScheduledDate ?? Load.ShipDate;
  public bool Completed => IsCompleted(Load);
  public bool InTransit => !Completed && (Load.Status == "in_transit" || Load.Stops.Any(x => x.PickedUpAt.HasValue));
  public bool Planned => Load.Status.Equals("planned", StringComparison.OrdinalIgnoreCase)
    || Load.Status.Equals("unassigned", StringComparison.OrdinalIgnoreCase);
  public int Column(DateOnly today) => InTransit ? 1 : Planned ? 0 : PickupDate == today || DeliveryDate == today ? 2 : 0;
  public string Status => Completed ? "Completed" : Load.Status.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
    ? "Unassigned" : Planned ? "Planned" : InTransit ? "In transit" : "Awaiting pickup";
  public string MapUrl => $"/fleet/map?truckId={Load.TruckId ?? Truck.TruckId}&dispatchId={Load.Id}";
  public static bool IsCompleted(DispatchResponse load) => load.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
    || load.Stops.OrderBy(stop => stop.Sequence).LastOrDefault() is { } final
      && (final.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase) || final.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase))
      && (final.DeliveredAt ?? final.DepartedAt).HasValue;
  private static bool StopCompleted(DispatchStopResponse? stop) => (stop?.DepartedAt ?? stop?.DeliveredAt ?? stop?.PickedUpAt).HasValue;
  public static string Text(string? value, string? fallback = null) => !string.IsNullOrWhiteSpace(value) ? value : !string.IsNullOrWhiteSpace(fallback) ? fallback : "—";
  public static string Location(DispatchStopResponse? stop) => stop is null ? "Pending" : Text(string.Join(", ", new[] { stop.City, stop.Province }.Where(x => !string.IsNullOrWhiteSpace(x))), stop.Address);
  public static string Schedule(DispatchStopResponse? stop, DateOnly? fallback = null)
  {
    var schedule = Client.Services.StopAppointmentDisplay.Format(stop?.ScheduledDate ?? fallback, stop?.ScheduledTime,
      stop?.IsWindow == true ? stop.ScheduledDate2 : null, stop?.IsWindow == true ? stop.ScheduledTime2 : null);
    return schedule == "—" ? "Date pending" : schedule;
  }
}
