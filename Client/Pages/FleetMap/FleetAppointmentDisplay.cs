using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Pages.FleetMap;

internal static class FleetAppointmentDisplay
{
  internal static (string? Date, string Value) Split(PlanStop? stop)
  {
    var text = StopAppointmentDisplay.Format(stop);
    var separator = text.IndexOf(" · ", StringComparison.Ordinal);
    var sameDay =
      stop?.ScheduledDate is not null
      && (
        stop.ScheduledDate2 is null || stop.ScheduledDate2 == stop.ScheduledDate
      );
    if (sameDay && separator >= 0)
      return (text[..separator], KeepClocksTogether(text[(separator + 3)..]));
    return (null, KeepClocksTogether(text));
  }

  private static string KeepClocksTogether(string value) =>
    value.Replace(" AM", "\u00a0AM").Replace(" PM", "\u00a0PM");
}
