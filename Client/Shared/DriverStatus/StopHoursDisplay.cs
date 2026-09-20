using System.Globalization;
using Client.Models.DTO.Planning;

namespace Client.Shared.DriverStatus;

public static class StopHoursDisplay
{
  public static string Signed(int? minutes) =>
    minutes is { } value
      ? $"{(value > 0 ? "+" : value < 0 ? "−" : "")}{Duration(value)}"
      : "—";

  public static string Duration(int minutes)
  {
    var absolute = Math.Abs((long)minutes);
    return string.Create(
      CultureInfo.InvariantCulture,
      $"{absolute / 60}h {absolute % 60:00}m"
    );
  }

  // Said in a sentence rather than read off a column, an hour that is not
  // there is not said: "Late by 45m", while the cycle keeps its aligned
  // "0h 45m" beside figures of other sizes. The estimate used to carry its
  // own wording for this, and the two differed under an hour.
  public static string Lateness(int minutes) =>
    Math.Abs((long)minutes) < 60
      ? string.Create(
        CultureInfo.InvariantCulture,
        $"{Math.Abs((long)minutes)}m"
      )
      : Duration(minutes);

  public static string Timestamp(DateTimeOffset value) =>
    value.ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture);

  public static string RecapDate(DateTimeOffset value) =>
    value.ToString("MMM d", CultureInfo.InvariantCulture);

  public static string AlternativeTone(StopHoursAlternative alternative) =>
    alternative.LateMinutes is > 0 ? "danger"
    : alternative.LateMinutes == 0 ? "success"
    : "muted";

  public static bool CycleShort(StopHoursForecast hours) =>
    hours.CycleVerified
    && (
      hours.FirstCycleShortageAt.HasValue
      || hours.DrivingShortfallMinutes is > 0
      || hours.CycleAtArrivalMinutes is < 0
      || hours.CurrentCycleMinutes is < 0
    );
}
