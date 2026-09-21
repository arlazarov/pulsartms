using Domain.Models.Eta;
using Domain.Models.Fleet;

namespace Domain.Rules.Eta;

public static class HosDutyStatus
{
  public static DriverDutyStatus? Read(
    HosHistory? history,
    DriverHosClocks? clocks,
    DateTimeOffset now,
    string? country = null
  )
  {
    var status =
      clocks?.UpdatedAt >= now.UtcDateTime.AddMinutes(-3)
        ? clocks.CurrentDutyStatus
        : null;
    DriverDutyStatus? Fallback() =>
      string.IsNullOrEmpty(status)
        ? null
        : new(status, null, null, clocks!.UpdatedAt);
    if (history is null || history.Through < now.AddMinutes(-3))
      return Fallback();
    var observed = history.Through < now ? history.Through : now;
    var periods = history
      .Periods.Where(p => p.Start < observed && p.End > p.Start)
      .OrderBy(p => p.Start)
      .ToList();
    if (periods.Count == 0)
      return Fallback();
    var latest = periods[^1];
    if (
      latest.End < observed.AddSeconds(-1)
      || !Known(latest.Status)
      || !string.IsNullOrEmpty(status) && status != latest.Status
    )
      return Fallback();
    var start = latest.Start;
    DateTimeOffset? rest = latest.Rest ? latest.Start : null;
    var cursor = latest.Start;
    var sameStatus = true;
    var restBoundary = !latest.Rest;
    for (var i = periods.Count - 2; i >= 0; i--)
    {
      var previous = periods[i];
      if (
        previous.End < cursor.AddSeconds(-1)
        || previous.End > cursor.AddSeconds(1)
        || !Known(previous.Status)
      )
      {
        if (!restBoundary)
          rest = null;
        break;
      }
      if (sameStatus && previous.Status == latest.Status)
        start = previous.Start;
      else
        sameStatus = false;
      if (!restBoundary)
      {
        if (previous.Rest)
          rest = previous.Start;
        else
          restBoundary = true;
      }
      if (!sameStatus && restBoundary)
        break;
      cursor = previous.Start;
    }
    if (!restBoundary)
      rest = null;
    var resetHours = country switch
    {
      "US" when history.UsCycle is null or { Days: 7 or 8, RestartHours: 34 } =>
        (int?)34,
      "CA" when history.CanadaCycle is { Days: 7, RestartHours: 36 } => 36,
      "CA" when history.CanadaCycle is { Days: 14, RestartHours: 72 } => 72,
      _ => null,
    };
    return new(
      latest.Status,
      start > history.From ? start : null,
      rest,
      observed
    )
    {
      CycleResetHours = rest.HasValue ? resetHours : null,
      CycleResetCountry = rest.HasValue && resetHours.HasValue ? country : null,
    };
  }

  private static bool Known(string status) =>
    status
      is "driving"
        or "onDuty"
        or "yardMove"
        or "offDuty"
        or "sleeperBerth"
        or "personalConveyance";
}
