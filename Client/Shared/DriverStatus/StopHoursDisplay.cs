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
