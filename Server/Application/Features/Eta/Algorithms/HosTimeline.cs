using Application.Features.Eta.Models;

namespace Application.Features.Eta.Algorithms;

public sealed class HosTimeline
{
  private readonly List<HosPeriod> periods;
  private readonly TimeZoneInfo zone;
  private readonly int dayStart;
  private readonly DateTimeOffset from;
  public HosHistory Source { get; }

  private HosTimeline(HosHistory source, List<HosPeriod> values)
  {
    Source = source;
    periods = values;
    from = source.From;
    dayStart = source.DayStartHour;
    zone = TimeZoneInfo.FindSystemTimeZoneById(source.TimeZoneId);
  }

  public static HosTimeline? Create(HosHistory? source, DateTimeOffset now)
  {
    if (
      source is null
      || source.Through < now.AddMinutes(-3)
      || source.DayStartHour is < 0 or > 23
    )
      return null;
    var values = new List<HosPeriod>();
    var cursor = source.From;
    foreach (var item in source.Periods.OrderBy(x => x.Start))
    {
      if (
        item.Status
        is not (
          "driving"
          or "onDuty"
          or "yardMove"
          or "offDuty"
          or "sleeperBerth"
          or "personalConveyance"
        )
      )
        return null;
      if (item.Start > cursor.AddSeconds(1))
        return null;
      if (
        values.Count > 0
        && item.Start < cursor
        && values[^1].Status != item.Status
      )
        return null;
      if (item.End <= cursor)
        continue;
      var value = item with { Start = cursor };
      if (values.Count > 0 && values[^1].Status == value.Status)
        values[^1] = values[^1] with { End = value.End };
      else
        values.Add(value);
      cursor = value.End;
    }
    if (cursor < source.Through.AddSeconds(-1) || values.Count == 0)
      return null;
    // Cover the small fetch gap conservatively using the latest known duty
    // status.
    if (cursor < now)
      values.Add(new(cursor, now, values[^1].Status));
    return new(source, values);
  }

  public void Add(DateTimeOffset start, DateTimeOffset end, string status)
  {
    if (end <= start)
      return;
    if (
      periods.Count > 0
      && periods[^1].End == start
      && periods[^1].Status == status
    )
      periods[^1] = periods[^1] with { End = end };
    else
      periods.Add(new(start, end, status));
  }

  public double Hours(
    DateTimeOffset start,
    DateTimeOffset end,
    Func<HosPeriod, bool> predicate
  ) =>
    periods
      .Where(predicate)
      .Sum(p =>
        Math.Max(
          0,
          (
            (p.End < end ? p.End : end) - (p.Start > start ? p.Start : start)
          ).TotalHours
        )
      );

  public DateTimeOffset DayBoundary(DateTimeOffset at)
  {
    var local = TimeZoneInfo.ConvertTime(at, zone).Date.AddHours(dayStart);
    var boundary = Boundary(local);
    return boundary > at ? Boundary(local.AddDays(-1)) : boundary;
  }

  private DateTimeOffset Boundary(DateTime local)
  {
    local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    while (zone.IsInvalidTime(local))
      local = local.AddMinutes(1);
    // During an ambiguous boundary choose the later occurrence: never release
    // early.
    var offset = zone.IsAmbiguousTime(local)
      ? zone.GetAmbiguousTimeOffsets(local).Min()
      : zone.GetUtcOffset(local);
    return new(local, offset);
  }

  public DateTimeOffset NextDay(DateTimeOffset at) =>
    Boundary(
      TimeZoneInfo.ConvertTime(DayBoundary(at), zone).DateTime.AddDays(1)
    );

  public double CycleUsed(DateTimeOffset at, HosCycleRule rule) =>
    CycleUsed(at, rule, LastCycleRestartAt(at, rule));

  public DateTimeOffset? LastCycleRestartAt(
    DateTimeOffset at,
    HosCycleRule rule
  ) =>
    Rests()
      .LastOrDefault(r =>
        r.End <= at && (r.End - r.Start).TotalHours >= rule.RestartHours
      )
      ?.End;

  public double CycleUsed(
    DateTimeOffset at,
    HosCycleRule rule,
    DateTimeOffset? restartAt
  )
  {
    var beginning = CycleWindowStart(at, rule);
    if (beginning < from)
      return double.NaN;
    if (restartAt is { } reset && reset > beginning)
      beginning = reset;
    return Hours(beginning, at, p => p.Duty);
  }

  public DateTimeOffset CycleWindowStart(
    DateTimeOffset at,
    HosCycleRule rule
  ) =>
    Boundary(
      TimeZoneInfo
        .ConvertTime(DayBoundary(at), zone)
        .DateTime.AddDays(1 - rule.Days)
    );

  public double DutySinceDailyRest(DateTimeOffset at)
  {
    var rest = Rests()
      .LastOrDefault(r => r.End <= at && (r.End - r.Start).TotalHours >= 24);
    return rest is null
      ? double.PositiveInfinity
      : Hours(rest.End, at, p => p.Duty);
  }

  public double OngoingRest(DateTimeOffset now) =>
    Rests().LastOrDefault(r => r.End == now) is { } r
      ? (r.End - r.Start).TotalHours
      : 0;

  private sealed record RestBlock(
    DateTimeOffset Start,
    DateTimeOffset End,
    double Sleeper
  );

  private List<RestBlock> Rests()
  {
    var result = new List<RestBlock>();
    foreach (var p in periods)
    {
      if (!p.Rest)
        continue;
      var sleeper =
        p.Status == "sleeperBerth" ? (p.End - p.Start).TotalHours : 0;
      if (result.Count > 0 && result[^1].End == p.Start)
        result[^1] = result[^1] with
        {
          End = p.End,
          Sleeper = Math.Max(result[^1].Sleeper, sleeper),
        };
      else
        result.Add(new(p.Start, p.End, sleeper));
    }
    return result;
  }

  public sealed record Split(
    double RestHours,
    double DriveLeft,
    double ShiftLeft
  );

  public Split? FindSplit(DateTimeOffset now, string country)
  {
    var rests = Rests();
    var current = rests.LastOrDefault(x => x.End == now);
    var workEnd = current?.Start ?? now;
    Split? best = null;
    foreach (
      var a in rests.Where(x => x.End <= workEnd && x.End > now.AddHours(-48))
    )
    {
      var hours = (a.End - a.Start).TotalHours;
      if (
        hours < 2
        || hours >= 10
        || (country == "CA" && a.Sleeper < hours - .00001)
      )
        continue;
      var anchor = rests.LastOrDefault(x =>
        x.End <= a.Start
        && (x.End - x.Start).TotalHours >= (country == "CA" ? 8 : 10)
      );
      if (anchor is null)
        continue;
      double driveLimit = country == "CA" ? 13 : 11;
      var totalDrive = Hours(anchor.End, workEnd, p => p.Driving);
      var elapsed = (workEnd - anchor.End).TotalHours - hours;
      if (
        totalDrive > driveLimit + .00001
        || elapsed > (country == "CA" ? 16 : 14) + .00001
        || country == "CA" && Hours(anchor.End, workEnd, p => p.Duty) > 14
      )
        continue;
      var needed =
        country == "CA" || a.Sleeper >= 7
          ? Math.Max(2, 10 - hours)
          : Math.Max(7, 10 - hours);
      // Completing the long portion requires consecutive sleeper, not generic
      // off duty.
      var existing =
        current is null ? 0
        : country == "CA" || a.Sleeper < 7 ? current.Sleeper
        : (current.End - current.Start).TotalHours;
      var rest = Math.Max(0, needed - existing);
      var driving = driveLimit - Hours(a.End, workEnd, p => p.Driving);
      var shift = (country == "CA" ? 16 : 14) - (workEnd - a.End).TotalHours;
      if (country == "CA")
        shift = Math.Min(shift, 14 - Hours(a.End, workEnd, p => p.Duty));
      if (driving <= .00001 || shift <= .00001 || rest >= 10)
        continue;
      var option = new Split(rest, driving, shift);
      if (best is null || option.RestHours < best.RestHours)
        best = option;
    }
    return best;
  }
}
