using System.Globalization;
using Client.Models.DTO.Planning;

namespace Client.Services;

public static class StopAppointmentDisplay
{
  public static string Format(PlanStop? stop)
  {
    if (stop is null)
      return "—";
    return Format(
      stop.ScheduledDate,
      stop.ScheduledTime,
      stop.ScheduledDate2,
      stop.ScheduledTime2
    );
  }

  public static string Format(
    DateOnly? start,
    TimeOnly? startTime,
    DateOnly? endDate,
    TimeOnly? endTime
  )
  {
    var end = endDate ?? (endTime.HasValue ? start : null);
    var format =
      start.HasValue && end.HasValue && start.Value.Year != end.Value.Year
        ? "MMM d, yyyy"
        : "MMM d";
    var firstTime = startTime?.ToString(
      "hh:mm tt",
      CultureInfo.InvariantCulture
    );
    var lastTime = endTime?.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    var sameDay = start == end;
    var first = Join(
      " · ",
      start?.ToString(format, CultureInfo.InvariantCulture),
      firstTime
    );
    var last = Join(
      " · ",
      !sameDay ? end?.ToString(format, CultureInfo.InvariantCulture) : null,
      sameDay && firstTime == lastTime ? null : lastTime
    );
    var result = Join(" – ", first, last == first ? null : last);
    return result.Length > 0 ? result : "—";
  }

  private static string Join(string separator, params string?[] values) =>
    string.Join(separator, values.Where(value => !string.IsNullOrEmpty(value)));
}
