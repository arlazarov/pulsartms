using System.Globalization;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;

namespace Client.Pages.Dispatch;

internal static class DispatchWorkspaceStopDisplay
{
  public static string Name(DispatchWorkspaceStop stop) =>
    string.IsNullOrWhiteSpace(stop.Name)
    || string.Equals(
      stop.Name.Trim(),
      "Transfer yard",
      StringComparison.OrdinalIgnoreCase
    )
      ? Address(stop)
      : stop.Name;

  public static string Timestamp(DateTime value) =>
    DateTime
      .SpecifyKind(value, DateTimeKind.Utc)
      .ToLocalTime()
      .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture);

  public static string Address(DispatchWorkspaceStop stop) =>
    string.Join(
      ", ",
      new[]
      {
        stop.Address,
        stop.City,
        stop.Province,
        stop.ZipCode,
        stop.Country,
      }.Where(value => !string.IsNullOrWhiteSpace(value))
    )
      is { Length: > 0 } address
      ? address
      : "Address not set";

  public static string Schedule(DispatchWorkspaceStop stop) =>
    stop.AppointmentMode == "unscheduled"
      ? "Not scheduled"
      : StopAppointmentDisplay.Format(
        stop.ScheduledDate,
        stop.ScheduledTime,
        stop.AppointmentMode == "window" ? stop.ScheduledDate2 : null,
        stop.AppointmentMode == "window" ? stop.ScheduledTime2 : null
      );

  public static string ReferenceLabel(DispatchWorkspaceStop stop) =>
    stop.Transfer is not null ? "Ref #"
    : stop.Job is "Pick Up" or "Pickup" ? "PU #"
    : stop.Job is "Delivery" or "Drop Off" ? "DEL #"
    : "Ref #";

  public static bool HasCargo(DispatchWorkspaceStop stop) =>
    stop.Transfer is null
    && stop.Job is "Pick Up" or "Pickup" or "Delivery" or "Drop Off";

  public static string Action(DispatchWorkspaceStop stop) =>
    stop.Transfer?.Action switch
    {
      "drop" => "Drop trailer",
      "hook" => "Hook trailer",
      "release" => "Release resources",
      "receive" => "Receive resources",
      _ => stop.Job,
    };
}
