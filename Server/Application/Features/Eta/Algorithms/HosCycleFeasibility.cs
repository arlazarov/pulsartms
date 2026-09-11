using Application.Features.Eta.Models;
using Application.Features.Fleet.Models;

namespace Application.Features.Eta.Algorithms;

public enum HosCycleMode { Observe, Recap, Restart }

public sealed class HosCycleFeasibility
{
  private const double Epsilon = .0000001;
  private readonly HosTimeline? timeline;
  private HosCycleRule? rule;
  private string country;
  private DateTimeOffset? restartAt;
  private DateTimeOffset anchorAt;
  private double anchorHours;
  private double historicalDuty;
  private double peakDrivingDebt;
  public bool Verified { get; private set; }
  public bool RecapVerified { get; private set; }
  public DateTimeOffset? FirstShortageAt { get; private set; }
  public string? UnavailableReason { get; private set; }
  public int? DrivingShortfallMinutes => Verified ? MinutesCeiling(peakDrivingDebt) : null;

  public HosCycleFeasibility(DateTimeOffset now, DriverHosClocks clocks, string country, HosHistory? history)
  {
    this.country = country;
    anchorAt = now;
    timeline = HosTimeline.Create(history, now);
    rule = timeline?.Source.Rule(country);
    if (rule is not null)
    {
      restartAt = timeline!.LastCycleRestartAt(now, rule);
      var used = timeline.CycleUsed(now, rule, restartAt);
      if (double.IsFinite(used) && clocks.CycleMs is { } cycle && cycle / 3600000d <= rule.Hours)
      {
        anchorHours = cycle / 3600000d;
        historicalDuty = used;
        Verified = true;
        RecapVerified = Math.Abs(rule.Hours - used - anchorHours) <= .25;
      }
      if (Verified && BalanceHours(now) is null) Verified = false;
    }
    if (!Verified)
    {
      RecapVerified = false;
      UnavailableReason = "Current ELD cycle or usable cycle history is unavailable.";
    }
  }

  public void Enter(string nextCountry)
  {
    if (nextCountry == country) return;
    var nextRule = timeline?.Source.Rule(nextCountry);
    if (Verified && RecapVerified && nextRule is not null && nextCountry is "US" or "CA")
    {
      var nextRestart = timeline!.LastCycleRestartAt(anchorAt, nextRule);
      var nextUsed = timeline.CycleUsed(anchorAt, nextRule, nextRestart);
      // A border must not award hours: both histories must reconcile to the same ELD anchor.
      if (double.IsFinite(nextUsed) && Math.Abs(nextRule.Hours - nextUsed - anchorHours) <= .25
        && (nextCountry != "CA" || nextRule.Days != 14 || double.IsFinite(timeline.DutySinceDailyRest(anchorAt))))
      {
        country = nextCountry;
        rule = nextRule;
        restartAt = nextRestart;
        historicalDuty = nextUsed;
        return;
      }
    }
    Verified = false;
    RecapVerified = false;
    UnavailableReason = "Cycle credits across different jurisdictions need verification.";
  }

  public double? BalanceHours(DateTimeOffset at)
  {
    if (!Verified) return null;
    var balance = CycleBalanceHours(at);
    if (country == "CA" && rule!.Days == 14)
      balance = Math.Min(balance, 70 - timeline!.DutySinceDailyRest(at));
    return double.IsFinite(balance) ? balance : null;
  }

  private double CycleBalanceHours(DateTimeOffset at)
  {
    if (!RecapVerified) return anchorHours - timeline!.Hours(anchorAt, at, p => p.Duty);
    var windowStart = timeline!.CycleWindowStart(at, rule!);
    var beginning = windowStart;
    if (restartAt is { } reset && reset > beginning) beginning = reset;
    var carried = 0d;
    if (beginning <= anchorAt && windowStart < anchorAt)
    {
      var eldUsed = rule!.Hours - anchorHours;
      var remaining = timeline.Hours(beginning, anchorAt, p => p.Duty);
      // Reconcile against ELD without releasing unexplained duty early or carrying it past its home day.
      carried = Math.Min(eldUsed, remaining + Math.Max(0, eldUsed - historicalDuty));
    }
    var projectedFrom = beginning > anchorAt ? beginning : anchorAt;
    return rule!.Hours - carried - timeline.Hours(projectedFrom, at, p => p.Duty);
  }

  public int? BalanceMinutes(DateTimeOffset at) => BalanceHours(at) is { } balance ? SignedMinutes(balance) : null;
  public DateTimeOffset? NextHomeDay(DateTimeOffset at) => Verified ? timeline!.NextDay(at) : null;

  public void Observe(DateTimeOffset start, DateTimeOffset end, string status)
  {
    if (timeline is null || end <= start) return;
    if (!Verified || status != "driving") { timeline.Add(start, end, status); return; }
    var cursor = start;
    while (cursor < end)
    {
      var boundary = timeline.NextDay(cursor);
      var segmentEnd = boundary < end ? boundary : end;
      var available = BalanceHours(cursor);
      var hours = (segmentEnd - cursor).TotalHours;
      if (available is { } balance && hours > Math.Max(0, balance) + Epsilon)
      {
        FirstShortageAt ??= cursor.AddHours(Math.Max(0, balance));
        peakDrivingDebt = Math.Max(peakDrivingDebt, hours - balance);
      }
      timeline.Add(cursor, segmentEnd, status);
      cursor = segmentEnd;
    }
  }

  public void CreditRestart(DateTimeOffset at)
  {
    if (!Verified || timeline!.OngoingRest(at) + Epsilon < rule!.RestartHours)
      throw new InvalidOperationException("A cycle scenario cannot credit an unfinished restart.");
    restartAt = at;
    anchorAt = at;
    anchorHours = rule!.Hours;
    historicalDuty = 0;
    RecapVerified = true;
  }

  public DateTimeOffset? NextUsableRecap(DateTimeOffset at)
  {
    if (!Verified || !RecapVerified) return null;
    var boundary = at;
    for (var day = 0; day < rule!.Days; day++)
    {
      boundary = timeline!.NextDay(boundary);
      var available = CycleBalanceHours(boundary);
      if (country == "CA" && rule.Days == 14)
      {
        var rest = timeline.OngoingRest(at) + (boundary - at).TotalHours;
        available = Math.Min(available, rest >= 24 ? 70 : 70 - timeline.DutySinceDailyRest(at));
      }
      if (double.IsFinite(available) && available > Epsilon) return boundary;
    }
    return null;
  }

  public StopCycleForecast? Snapshot(DateTimeOffset at)
  {
    if (BalanceHours(at) is not { } balance) return null;
    if (!RecapVerified) return new(Math.Max(0, SignedMinutes(balance)), null, null, null, false);
    var available = balance;
    var boundary = at;
    for (var day = 0; day < rule!.Days; day++)
    {
      boundary = timeline!.NextDay(boundary);
      if (BalanceHours(boundary) is not { } future) break;
      var returning = SignedMinutes(future - available);
      if (returning > 0)
        return new(Math.Max(0, SignedMinutes(balance)), boundary, returning, timeline.Source.TimeZoneId, true);
      available = future;
    }
    return new(Math.Max(0, SignedMinutes(balance)), null, null, timeline!.Source.TimeZoneId, true);
  }

  private static int SignedMinutes(double hours) => (int)Math.Clamp(Math.Floor(hours * 60 + Epsilon), int.MinValue, int.MaxValue);
  private static int MinutesCeiling(double hours) => (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, hours) * 60 - Epsilon));
}

internal sealed class CycleScenarioUnavailableException(string reason = "A verified cycle alternative is unavailable.") : Exception(reason);
