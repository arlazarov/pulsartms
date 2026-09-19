using System.Globalization;
using Client.Models.DTO.Dispatch.Workspace;

namespace Client.Pages.Dispatch;

public sealed class DispatchStopClockDraft
{
  public string Start { get; set; } = "";
  public string End { get; set; } = "";

  public static DispatchStopClockDraft From(DispatchWorkspaceStop stop) =>
    new()
    {
      Start = Format(stop.ScheduledTime),
      End = Format(stop.ScheduledTime2),
    };

  public bool Apply(DispatchWorkspaceStop stop, out string? error)
  {
    error = null;
    if (stop.AppointmentMode == "unscheduled")
      return true;
    var startUnchanged = Start == Format(stop.ScheduledTime);
    var endUnchanged = End == Format(stop.ScheduledTime2);
    if (!TryTime(Start, out var start))
    {
      error = "Enter the start time as 02:00 PM, or leave it blank.";
      return false;
    }
    TimeOnly? end = null;
    if (stop.AppointmentMode == "window" && !TryTime(End, out end))
    {
      error = "Enter the end time as 02:00 PM, or leave it blank.";
      return false;
    }
    if (!startUnchanged)
      stop.ScheduledTime = start;
    if (!endUnchanged || stop.AppointmentMode != "window")
      stop.ScheduledTime2 = end;
    return true;
  }

  public static string Format(TimeOnly? time) =>
    time?.ToString(
      time.Value.Second > 0 ? "hh:mm:ss tt" : "hh:mm tt",
      CultureInfo.InvariantCulture
    ) ?? "";

  private static bool TryTime(string text, out TimeOnly? value)
  {
    value = null;
    if (string.IsNullOrWhiteSpace(text))
      return true;
    if (
      !TimeOnly.TryParseExact(
        text.Trim(),
        ["hh:mm tt", "h:mm tt", "hh:mm:ss tt", "h:mm:ss tt"],
        CultureInfo.InvariantCulture,
        DateTimeStyles.AllowWhiteSpaces,
        out var time
      )
    )
      return false;
    value = time;
    return true;
  }
}
