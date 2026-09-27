using System.Globalization;
using Client.Models.DTO.Dispatch;

namespace Client.Pages.FleetMap;

// When a stop the server says is completed was completed, for the stop
// cards. Completion itself is only ever the server's IsCompleted, never GPS
// passage or the load's own state. The moment is the recorded event that
// closed the stop: a manual completion, else departure, else the delivery
// or pickup; none known says nothing rather than guessing.
internal static class StopCompletion
{
  public static string? Time(DispatchStopResponse stop)
  {
    if (!stop.IsCompleted)
      return null;
    var at =
      stop.ManualCompletedAt
      ?? stop.DepartedAt
      ?? stop.DeliveredAt
      ?? stop.PickedUpAt;
    return at is { } value
      ? DateTime
        .SpecifyKind(value, DateTimeKind.Utc)
        .ToLocalTime()
        .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture)
      : null;
  }
}
