using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Models;

namespace Server.Tests.Support;

internal static class HosForecastFixture
{
  public static HosHistory History(DateTimeOffset now, double cycleHours = 0, double firstDayHours = 0,
    double ongoingRestHours = 0)
  {
    var endHour = now.TimeOfDay.TotalHours - ongoingRestHours;
    var todayHours = Math.Min(10, endHour);
    var middleHours = (70 - cycleHours - firstDayHours - todayHours) / 6;
    var start = new DateTimeOffset(now.Date.AddDays(-16), now.Offset);
    var cursor = start;
    var periods = new List<HosPeriod>();
    for (var day = -7; day <= 0; day++)
    {
      var hours = day == -7 ? firstDayHours : day == 0 ? todayHours : middleHours;
      if (hours == 0) continue;
      var end = new DateTimeOffset(now.Date.AddDays(day).AddHours(endHour), now.Offset);
      var begin = end.AddHours(-hours);
      if (cursor < begin) periods.Add(new(cursor, begin, "offDuty"));
      periods.Add(new(begin, end, "onDuty"));
      cursor = end;
    }
    if (cursor < now) periods.Add(new(cursor, now, "offDuty"));
    return new(start, now, "Etc/UTC", 0, new(8, 70, 34), null, periods);
  }

  public static DriverHosClocks Clocks(DateTimeOffset now, HosHistory history, double drive = 11, double shift = 14)
  {
    var rule = history.UsCycle!;
    var used = HosTimeline.Create(history, now)!.CycleUsed(now, rule);
    return new() { DriveMs = (long)(drive * 3600000), ShiftMs = (long)(shift * 3600000),
      CycleMs = (long)Math.Round((rule.Hours - used) * 3600000), BreakMs = 8 * 3600000L,
      UpdatedAt = now.UtcDateTime };
  }

  public static HosTravelClock Clock(DateTimeOffset now, HosCycleMode mode, double firstDayHours = 0,
    double ongoingRestHours = 0, EtaPlanningOptions? planning = null)
  {
    var history = History(now, firstDayHours: firstDayHours, ongoingRestHours: ongoingRestHours);
    return new(now, Clocks(now, history), "US", history, planning, mode);
  }
}
